using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Dati;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Fiscale;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Vendita
{
    /// <summary>
    /// ServizioVendita — le fasi dell'emissione dello scontrino fiscale, nell'ordine del protocollo:
    /// validazione → RT pronto → righe → sconti → (appendice) → chiusura → verifica → appendice/cassetto.
    /// Ogni fase restituisce null se si prosegue, altrimenti l'esito con cui fermarsi.
    /// </summary>
    public partial class ServizioVendita
    {
        /// <summary>Dati di una emissione, condivisi tra le fasi.</summary>
        private sealed class Emissione
        {
            public Impostazioni Cfg;
            public Pagamento Pagamento;
            public int Eliminacode;
            public List<RigaScontrino> Righe;      // solo quelle che contano nel totale
            public List<RigaScontrino> Prodotti;
            public int TotaleCent, ContantiCent, CartaCent, RestoCent;
        }

        // ══════════════ 1. VALIDAZIONE E PAGAMENTO ══════════════

        private EsitoVendita Valida(Emissione e, List<RigaScontrino> righe)
        {
            // per lo storico anche i gusti delle combinazioni (prezzo 0), come il PC
            var perArchivio = righe.Where(r => r.ContaNelTotale || r.Tipo == TipoRiga.Gusto).ToList();
            e.Righe = righe.Where(r => r.ContaNelTotale).ToList(); // subtotale e separatori sono solo visivi
            e.Prodotti = e.Righe.Where(r => r.Tipo == TipoRiga.Prodotto).ToList();
            e.TotaleCent = e.Righe.Sum(r => r.ImportoCentesimi);

            if (e.Prodotti.Count == 0)
                return EsitoVendita.Errore("ATTENZIONE", "Lo scontrino è vuoto.");
            var rigaNonValida = e.Prodotti.FirstOrDefault(r => r.PrezzoCentesimi <= 0 || r.ImportoCentesimi <= 0);
            if (rigaNonValida != null)
                return EsitoVendita.Errore("RIGA NON VALIDA",
                    $"L'articolo \"{rigaNonValida.Descrizione.Trim()}\" ha prezzo € {rigaNonValida.PrezzoCentesimi / 100m:N2}.\n\n" +
                    "Correggi il prezzo (o chiudi la combinazione) prima di emettere lo scontrino.");
            if (e.TotaleCent <= 0)
                return EsitoVendita.Errore("TOTALE NON VALIDO", $"Il totale è € {e.TotaleCent / 100m:N2}.\nCorreggere gli sconti.");

            var pag = e.Pagamento;
            switch (pag.Metodo)
            {
                case MetodiPagamento.Contanti:
                    if (pag.VersatoCent > 0 && pag.VersatoCent < e.TotaleCent)
                        return EsitoVendita.Errore("IMPORTO INSUFFICIENTE",
                            $"Richiesto: € {e.TotaleCent / 100m:N2}\nVersato: € {pag.VersatoCent / 100m:N2}");
                    e.RestoCent = pag.VersatoCent > 0 ? pag.VersatoCent - e.TotaleCent : 0;
                    e.ContantiCent = e.TotaleCent;
                    break;
                case MetodiPagamento.Misto:
                    if (pag.MistoContantiCent <= 0 || pag.MistoContantiCent >= e.TotaleCent)
                        return EsitoVendita.Errore("PAGAMENTO MISTO", "La parte in contanti deve essere maggiore di zero e minore del totale.");
                    e.ContantiCent = pag.MistoContantiCent;
                    e.CartaCent = e.TotaleCent - e.ContantiCent;
                    break;
                default:
                    e.CartaCent = e.TotaleCent;
                    break;
            }

            LogManager.Info($"VENDITA BANCO: totale €{e.TotaleCent / 100m:F2}, metodo {pag.Metodo}, righe {e.Prodotti.Count}");

            _ultimoDaRegistrare = new VenditaRegistrata
            {
                TotaleCentesimi = e.TotaleCent,
                MetodoPagamento = pag.Metodo,
                ContantiCent = e.ContantiCent,
                CartaCent = e.CartaCent,
                StampatoSuRT = true,
                Righe = perArchivio
            };
            _ultimoResto = e.RestoCent;
            return null;
        }

        // ══════════════ 2. RT PRONTO: connessione, controllo, TX_FOOTER, operatore ══════════════

        private async Task<EsitoVendita> PreparaRTAsync(IFiscalConnector rt, Emissione e)
        {
            var cfg = e.Cfg;
            if (!await rt.ConnectAsync())
            {
                LogManager.Error("Connessione RT fallita");
                return EsitoVendita.Errore("REGISTRATORE NON RAGGIUNGIBILE",
                    $"Impossibile collegarsi al registratore {cfg.IP_RT}:{cfg.Porta_RT}.\n\n" +
                    (string.IsNullOrEmpty(rt.UltimoErrore) ? "" : $"⚠️ {rt.UltimoErrore}\n\n") +
                    "Verificare che sia acceso e sulla stessa rete Wi-Fi.\nLo scontrino NON è stato emesso.");
            }

            // ── HEALTH CHECK dopo una vendita fallita ──
            if (_ultimaVenditaFallita)
            {
                LogManager.Info("HEALTH CHECK: recovery preventivo...");
                if (!await rt.RecuperaScontrinoApertoAsync())
                {
                    string err = rt.UltimoErrore;
                    rt.Disconnect();
                    return EsitoVendita.Errore("RT BLOCCATO",
                        (string.IsNullOrEmpty(err) ? "" : $"⚠️ {err}\n\n") +
                        "Il registratore ha uno scontrino bloccato dalla vendita precedente.\n\n" +
                        "1. Verificare coperchio e carta\n2. Usare 'FUNZIONI CASSA → RESET RT'\n3. Se persiste, spegnere e riaccendere il registratore");
                }
                _ultimaVenditaFallita = false;
            }

            await ConfiguraTxFooterAsync(rt, cfg);

            // ── OPERATORE ──
            if (!await rt.SelezionaOperatoreAsync(cfg.OperatoreRT))
            {
                string errOp = rt.UltimoErrore;
                if (!string.IsNullOrEmpty(errOp) && errOp.Contains("COPERCHIO"))
                {
                    rt.Disconnect();
                    _ultimaVenditaFallita = true;
                    return EsitoVendita.Errore("ERRORE STAMPANTE", $"⚠️ {errOp}\n\nVerificare il registratore e riprovare.");
                }
                LogManager.Warning("RT ha rifiutato selezione operatore — continuo comunque");
            }
            return null;
        }

        /// <summary>
        /// TX_FOOTER va configurato una volta per ogni registratore (se cambia tipo/IP in impostazioni, si rifà).
        /// Memorizzato se l'RT accetta; dopo un rifiuto si riprova una sola volta (RT che non lo supporta: non si rallenta ogni vendita).
        /// </summary>
        private async Task ConfiguraTxFooterAsync(IFiscalConnector rt, Impostazioni cfg)
        {
            string chiaveRT = $"{cfg.TipoCassa}|{cfg.IP_RT}|{cfg.Porta_RT}";
            if (_txFooterConfiguratoPer == chiaveRT) return;
            bool ok = await rt.ConfiguraTxFooterJsonAsync();
            if (ok || _txFooterTentativo == chiaveRT) _txFooterConfiguratoPer = chiaveRT;
            if (!ok)
            {
                _txFooterTentativo = chiaveRT;
                LogManager.Warning("Configurazione TX_FOOTER non accettata dall'RT");
                try { await rt.RecuperaScontrinoApertoAsync(); } catch { }   // azzera NAK/XOFF lasciati dal rifiuto
            }
        }

        // ══════════════ 3. RIGHE E SCONTI ══════════════

        private async Task<EsitoVendita> InviaRigheAsync(IFiscalConnector rt, Emissione e)
        {
            var cfg = e.Cfg;
            var daInviare = cfg.AccorpaRigheScontrino ? AccorpamentoRighe.Accorpa(e.Prodotti) : e.Prodotti;
            _fase = 1;
            foreach (var riga in daInviare)
            {
                // 3i ed Epson inviano ASCII: "CAFFÈ" o "PIZZA ½+½" verrebbero rifiutati o stampati male (È → E', ½ → 1/2)
                string descr = TestoRT.SenzaAccenti(riga.Descrizione);
                LogManager.Debug($"Riga: Rep{riga.RepartoFiscale} {descr} €{riga.PrezzoCentesimi / 100.0:F2} x{riga.Quantita}");
                if (!await rt.VendiRepartoAsync(riga.RepartoFiscale, descr, riga.PrezzoCentesimi, riga.Quantita))
                {
                    LogManager.Error($"RT ha RIFIUTATO riga: {riga.Descrizione}");
                    return await AnnullaEdEsci(rt, "ERRORE REGISTRATORE",
                        "Il registratore ha rifiutato un articolo.\n\n" +
                        "Verificare coperchio, carta e reparti/aliquote configurati sull'RT.");
                }
                // varianti con prezzo su un articolo a peso: importo fisso, riga a parte (o sconto sulla riga se in meno)
                if (riga.SovrapprezzoCent > 0
                        ? !await rt.VendiRepartoAsync(riga.RepartoFiscale, "VARIANTI", riga.SovrapprezzoCent, 1)
                        : riga.SovrapprezzoCent < 0 && !await rt.ScontoValoreRigaAsync(-riga.SovrapprezzoCent))
                {
                    LogManager.Error($"RT ha RIFIUTATO le varianti di: {riga.Descrizione}");
                    return await AnnullaEdEsci(rt, "ERRORE REGISTRATORE", "Il registratore ha rifiutato le varianti di un articolo a peso.");
                }
                // Varianti su scontrino fiscale (se configurato), come il PC: righe di messaggio sotto l'articolo
                if (cfg.VariantiSuScontrino && !string.IsNullOrEmpty(riga.VariantiTesto))
                    foreach (var vt in riga.VariantiTesto.Split('|'))
                        if (!string.IsNullOrWhiteSpace(vt))
                            await rt.StampaMessaggioAsync(TestoRT.SenzaAccenti(vt.Trim()));
            }
            return null;
        }

        private async Task<EsitoVendita> InviaScontiAsync(IFiscalConnector rt, Emissione e)
        {
            var sconti = e.Righe.Where(r => r.Tipo == TipoRiga.Sconto).ToList();
            if (sconti.Count == 0) return null;
            bool ok = await rt.SubtotaleAsync();
            foreach (var s in sconti)
            {
                if (!ok) break;
                ok = s.ScontoPercentualeCent.HasValue
                    ? await rt.ScontoPercentualeTotaleAsync(s.ScontoPercentualeCent.Value / 100m)
                    : await rt.ScontovaloreTotaleAsync(-s.PrezzoCentesimi);
            }
            return ok ? null : await AnnullaEdEsci(rt, "ERRORE SCONTI", "Il registratore ha rifiutato lo sconto.");
        }

        /// <summary>Appendice eliminacode (comanda da banco), come il PC: annunciata prima della chiusura. Non critica.</summary>
        private static async Task PreannunciaAppendiceAsync(IFiscalConnector rt, Emissione e)
        {
            if (e.Eliminacode <= 0) return;
            try { if (!await rt.PreannunciaAppendiceAsync(e.Eliminacode)) LogManager.Warning("Appendice eliminacode non accettata"); }
            catch (Exception ex) { LogManager.Warning($"Appendice eliminacode (non critico): {ex.Message}"); }
        }

        // ══════════════ 4. CHIUSURA E VERIFICA ══════════════

        private async Task<EsitoVendita> ChiudiAsync(IFiscalConnector rt, Emissione e)
        {
            // Importo esatto e parte carta del misto: chiusura "a saldo" calcolata dall'RT,
            // così un centesimo di arrotondamento sugli sconti % non lascia lo scontrino aperto.
            _fase = 2;
            var pag = e.Pagamento;
            bool chiusuraOk = pag.Metodo switch
            {
                MetodiPagamento.Misto => await rt.ChiudiMistoAsync(e.ContantiCent, 0),
                MetodiPagamento.Contanti when pag.VersatoCent > e.TotaleCent => await rt.ChiudiContantiAsync(pag.VersatoCent),
                MetodiPagamento.Contanti => await rt.ChiudiContantiAutoAsync(),
                _ => await rt.ChiudiCartaCreditoAsync()
            };
            if (chiusuraOk)
            {
                // Epson: la risposta positiva alla chiusura È la conferma (un errore dopo non deve dire "da verificare")
                if (e.Cfg.TipoCassa == TipoCassaFiscale.Epson) _fase = 3;
                return null;
            }

            // Chiusura inviata ma senza conferma (rifiuto o risposta persa): NON si annulla in automatico,
            // altrimenti uno scontrino magari già stampato verrebbe rifatto (doppio scontrino).
            // Decide l'operatore; se non è uscito, alla prossima vendita il controllo iniziale sblocca l'RT.
            string err = rt.UltimoErrore;
            LogManager.Error($"RT: chiusura senza conferma ({err})");
            return Incerto(rt, e, "Il registratore non ha confermato la chiusura dello scontrino." +
                                  (string.IsNullOrEmpty(err) ? "" : $"\n⚠️ {err}") +
                                  "\n\nControllare il registratore.");
        }

        /// <summary>Lettura della conferma (TX_FOOTER); senza conferma si controlla se lo scontrino è rimasto aperto.</summary>
        private async Task<(EsitoVendita fermo, RisultatoChiusuraRT footer)> VerificaChiusuraAsync(IFiscalConnector rt, Emissione e)
        {
            // _fase resta 2 finché la chiusura non è certa: un'eccezione qui significa "da verificare", mai "emesso"
            var footer = await rt.LeggiTxFooterAsync();
            if (footer.Confermato)
            {
                _fase = 3;
                LogManager.Info($"✅ SCONTRINO CONFERMATO: Doc={footer.NumeroDocumento}, Azz={footer.NumeroAzzeramento}");
                return (null, footer);
            }

            LogManager.Warning($"⚠️ TX_FOOTER non confermato: {footer.Errore ?? "risposta assente"}");
            // nessuna conferma: se lo scontrino è ancora APERTO la chiusura non è avvenuta → si annulla e la vendita resta a video
            bool? aperto = null;
            try { aperto = await rt.StatoScontrinoDopoChiusuraAsync(); }
            catch (Exception ex) { LogManager.Warning($"Verifica scontrino aperto: {ex.Message}"); }
            if (aperto == true)
            {
                LogManager.Error("RT: dopo la chiusura lo scontrino risulta ancora aperto");
                try
                {
                    return (await AnnullaEdEsci(rt, "SCONTRINO NON CHIUSO",
                        "Il registratore non ha chiuso lo scontrino (carta, coperchio o pagamento rifiutato).\n\nLo scontrino è stato annullato: la vendita resta a video, riprova."), footer);
                }
                catch (Exception ex)
                {
                    // rete caduta durante l'annullo: non si sa com'è finita → decide l'operatore
                    LogManager.Error($"RT: annullo dopo chiusura non confermata interrotto: {ex.Message}");
                    return (Incerto(rt, e, "La comunicazione si è interrotta mentre si annullava uno scontrino rimasto aperto.\n\nControllare il registratore."), footer);
                }
            }
            if (aperto == null && e.Cfg.TipoCassa == TipoCassaFiscale.RT3i)
            {
                // né conferma né stato leggibile: non si dà per buono, decide l'operatore guardando l'RT
                LogManager.Error("RT: chiusura non confermata e stato scontrino non leggibile");
                return (Incerto(rt, e, "Il registratore non ha confermato lo scontrino e non risponde (carta o coperchio?).\n\nControllare il registratore."), footer);
            }
            _fase = 3;   // stato "chiuso" letto (3i) o chiusura già confermata dalla risposta (Epson)
            return (null, footer);
        }

        /// <summary>Esito "da verificare": decide l'operatore guardando l'RT; la vendita successiva controlla prima l'RT.</summary>
        private EsitoVendita Incerto(IFiscalConnector rt, Emissione e, string messaggio)
        {
            _ultimaVenditaFallita = true;
            rt.Disconnect();
            return new EsitoVendita
            {
                Stato = StatoEsito.Incerto,
                Titolo = "VERIFICARE LO SCONTRINO",
                Messaggio = messaggio,
                DaRegistrare = _ultimoDaRegistrare,
                RestoCent = e.RestoCent
            };
        }

        // ══════════════ 5. DOPO LA CHIUSURA (non critiche) ══════════════

        private static async Task OperazioniDopoChiusuraAsync(IFiscalConnector rt, Emissione e)
        {
            if (e.Eliminacode > 0)
            {
                try { await rt.StampaAppendiceAsync(e.Eliminacode); }
                catch (Exception ex) { LogManager.Warning($"Stampa appendice eliminacode (non critico): {ex.Message}"); }
            }
            if (e.Cfg.ApriCassetto)
            {
                // scontrino già chiuso: un errore del cassetto non deve far perdere il numero documento letto
                try { await rt.ApriCassettoAsync(); }
                catch (Exception ex) { LogManager.Warning($"Apertura cassetto non riuscita: {ex.Message}"); }
            }
        }
    }
}
