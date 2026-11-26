using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Dati;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Fiscale;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Vendita
{
    /// <summary>Come vuole pagare il cliente.</summary>
    public class Pagamento
    {
        public string Metodo { get; set; } = MetodiPagamento.Contanti;
        /// <summary>Contanti: importo consegnato dal cliente (0 = importo esatto).</summary>
        public int VersatoCent { get; set; }
        /// <summary>Misto: parte in contanti (il resto va su carta).</summary>
        public int MistoContantiCent { get; set; }

        public static Pagamento Contanti(int versatoCent = 0) => new Pagamento { Metodo = MetodiPagamento.Contanti, VersatoCent = versatoCent };
        public static Pagamento Carta() => new Pagamento { Metodo = MetodiPagamento.Carta };
        public static Pagamento Misto(int contantiCent) => new Pagamento { Metodo = MetodiPagamento.Misto, MistoContantiCent = contantiCent };
    }

    public enum StatoEsito
    {
        /// <summary>Scontrino emesso e vendita salvata.</summary>
        Riuscito,
        /// <summary>Scontrino emesso ma salvataggio locale fallito (l'incasso c'è, manca il dato).</summary>
        RiuscitoSenzaArchivio,
        /// <summary>Niente è stato emesso: il carrello va lasciato com'è.</summary>
        Fallito,
        /// <summary>
        /// La comunicazione si è interrotta DOPO l'invio della chiusura: lo scontrino potrebbe
        /// essere uscito. L'operatore deve controllare prima di riprovare (evita doppi scontrini).
        /// </summary>
        Incerto
    }

    public class EsitoVendita
    {
        public StatoEsito Stato { get; set; }
        public string Titolo { get; set; } = "";
        public string Messaggio { get; set; } = "";
        public int RestoCent { get; set; }
        public int NumeroDocumento { get; set; }
        public int NumeroAzzeramento { get; set; }

        public bool Emesso => Stato == StatoEsito.Riuscito || Stato == StatoEsito.RiuscitoSenzaArchivio;

        /// <summary>Dati della vendita da registrare se l'operatore conferma che lo scontrino è uscito (stato Incerto).</summary>
        public VenditaRegistrata DaRegistrare { get; set; }

        public static EsitoVendita Errore(string titolo, string messaggio) =>
            new EsitoVendita { Stato = StatoEsito.Fallito, Titolo = titolo, Messaggio = messaggio };
    }

    /// <summary>
    /// Emissione dello scontrino fiscale dal banco.
    /// Stessa sequenza collaudata di MainWindow.EseguiVenditaAsync (POS Touch v17):
    /// health check → operatore → righe → sconti → chiusura → TX_FOOTER → cassetto → salvataggio.
    /// Non mostra nulla a video: restituisce un <see cref="EsitoVendita"/> che la UI visualizza.
    /// </summary>
    public class ServizioVendita
    {
        private readonly Func<Impostazioni> _impostazioni;
        private readonly ArchivioVendite _archivio;

        private int _inCorso;                 // 1 = vendita in corso (blocca doppio tocco)
        private bool _ultimaVenditaFallita;   // FIX #16 originale: recovery preventivo alla vendita successiva
        private string _txFooterConfiguratoPer;  // tipo|ip|porta del registratore già configurato
        private int _fase;                       // 0 = prima delle righe, 1 = righe inviate, 2 = chiusura inviata, 3 = chiuso

        public ServizioVendita(Func<Impostazioni> impostazioni, ArchivioVendite archivio)
        {
            _impostazioni = impostazioni;
            _archivio = archivio;
        }

        public bool InCorso => Volatile.Read(ref _inCorso) == 1;

        /// <param name="eliminacode">Comanda da banco: numero stampato in appendice allo scontrino (0 = nessuno).</param>
        public async Task<EsitoVendita> EmettiAsync(List<RigaScontrino> righe, Pagamento pagamento, int eliminacode = 0)
        {
            if (Interlocked.CompareExchange(ref _inCorso, 1, 0) != 0)
                return EsitoVendita.Errore("ATTENDERE", "Vendita in corso, attendere il completamento.");
            bool rtPreso = false;
            try
            {
                rtPreso = await AccessoRT.PrendiAsync(TimeSpan.FromSeconds(10));
                if (!rtPreso) return EsitoVendita.Errore("REGISTRATORE OCCUPATO", AccessoRT.MessaggioOccupato);
                return await EmettiInternoAsync(righe, pagamento, eliminacode);
            }
            catch (Exception ex)
            {
                LogManager.Error($"Vendita: errore imprevisto (fase {_fase}): {ex}");
                _ultimaVenditaFallita = true;
                if (_fase >= 3)
                {
                    // Scontrino già chiuso dall'RT: l'errore è avvenuto dopo (lettura numero, cassetto)
                    var e3 = RegistraEsito(_ultimoDaRegistrare, null, StatoEsito.Riuscito);
                    e3.RestoCent = _ultimoResto;
                    return e3;
                }
                if (_fase == 2)
                {
                    return new EsitoVendita
                    {
                        Stato = StatoEsito.Incerto,
                        Titolo = "VERIFICARE LO SCONTRINO",
                        Messaggio = "La comunicazione con il registratore si è interrotta durante la chiusura.\n\n" +
                                    "Lo scontrino potrebbe essere stato stampato.\nControllare il registratore.",
                        DaRegistrare = _ultimoDaRegistrare
                    };
                }
                return EsitoVendita.Errore("ERRORE DI COMUNICAZIONE",
                    $"Comunicazione con il registratore interrotta:\n{ex.Message}\n\n" +
                    "Lo scontrino NON è stato chiuso. Alla prossima vendita verrà eseguito un controllo automatico del registratore.");
            }
            finally
            {
                if (rtPreso) AccessoRT.Rilascia();
                Interlocked.Exchange(ref _inCorso, 0);
            }
        }

        private VenditaRegistrata _ultimoDaRegistrare;
        private int _ultimoResto;

        private async Task<EsitoVendita> EmettiInternoAsync(List<RigaScontrino> righe, Pagamento pag, int eliminacode)
        {
            _fase = 0;
            _ultimoDaRegistrare = null;
            var cfg = _impostazioni();
            // per lo storico anche i gusti delle combinazioni (prezzo 0), come il PC
            var perArchivio = righe.Where(r => r.ContaNelTotale || r.Tipo == TipoRiga.Gusto).ToList();
            righe = righe.Where(r => r.ContaNelTotale).ToList(); // subtotale e separatori sono solo visivi
            var prodotti = righe.Where(r => r.Tipo == TipoRiga.Prodotto).ToList();
            int totaleCent = righe.Sum(r => r.ImportoCentesimi);

            // ── VALIDAZIONE ──
            if (prodotti.Count == 0)
                return EsitoVendita.Errore("ATTENZIONE", "Lo scontrino è vuoto.");
            var rigaNonValida = prodotti.FirstOrDefault(r => r.PrezzoCentesimi <= 0 || r.ImportoCentesimi <= 0);
            if (rigaNonValida != null)
                return EsitoVendita.Errore("RIGA NON VALIDA",
                    $"L'articolo \"{rigaNonValida.Descrizione.Trim()}\" ha prezzo € {rigaNonValida.PrezzoCentesimi / 100m:N2}.\n\n" +
                    "Correggi il prezzo (o chiudi la combinazione) prima di emettere lo scontrino.");
            if (totaleCent <= 0)
                return EsitoVendita.Errore("TOTALE NON VALIDO", $"Il totale è € {totaleCent / 100m:N2}.\nCorreggere gli sconti.");

            int restoCent = 0;
            int contantiCent = 0, cartaCent = 0;
            switch (pag.Metodo)
            {
                case MetodiPagamento.Contanti:
                    if (pag.VersatoCent > 0 && pag.VersatoCent < totaleCent)
                        return EsitoVendita.Errore("IMPORTO INSUFFICIENTE",
                            $"Richiesto: € {totaleCent / 100m:N2}\nVersato: € {pag.VersatoCent / 100m:N2}");
                    restoCent = pag.VersatoCent > 0 ? pag.VersatoCent - totaleCent : 0;
                    contantiCent = totaleCent;
                    break;
                case MetodiPagamento.Misto:
                    if (pag.MistoContantiCent <= 0 || pag.MistoContantiCent >= totaleCent)
                        return EsitoVendita.Errore("PAGAMENTO MISTO", "La parte in contanti deve essere maggiore di zero e minore del totale.");
                    contantiCent = pag.MistoContantiCent;
                    cartaCent = totaleCent - contantiCent;
                    break;
                default:
                    cartaCent = totaleCent;
                    break;
            }

            LogManager.Info($"VENDITA BANCO: totale €{totaleCent / 100m:F2}, metodo {pag.Metodo}, righe {prodotti.Count}");

            _ultimoDaRegistrare = new VenditaRegistrata
            {
                TotaleCentesimi = totaleCent,
                MetodoPagamento = pag.Metodo,
                ContantiCent = contantiCent,
                CartaCent = cartaCent,
                StampatoSuRT = true,
                Righe = perArchivio
            };
            int restoFinale = restoCent;
            _ultimoResto = restoCent;

            using var rt = FiscalConnectorFactory.Create(cfg.TipoCassa, cfg.IP_RT, cfg.Porta_RT);

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

            // TX_FOOTER va configurato una volta per ogni registratore (se cambia tipo/IP in impostazioni, si rifà)
            string chiaveRT = $"{cfg.TipoCassa}|{cfg.IP_RT}|{cfg.Porta_RT}";
            if (_txFooterConfiguratoPer != chiaveRT)
            {
                // memorizzato solo se l'RT ha accettato: altrimenti si riprova alla vendita dopo
                if (await rt.ConfiguraTxFooterJsonAsync()) _txFooterConfiguratoPer = chiaveRT;
                else LogManager.Warning("Configurazione TX_FOOTER non accettata: si riprova alla prossima vendita");
            }

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

            // ── RIGHE ──
            var daInviare = cfg.AccorpaRigheScontrino ? AccorpamentoRighe.Accorpa(prodotti) : prodotti;
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
                // Varianti su scontrino fiscale (se configurato), come il PC: righe di messaggio sotto l'articolo
                if (cfg.VariantiSuScontrino && !string.IsNullOrEmpty(riga.VariantiTesto))
                    foreach (var vt in riga.VariantiTesto.Split('|'))
                        if (!string.IsNullOrWhiteSpace(vt))
                            await rt.StampaMessaggioAsync(TestoRT.SenzaAccenti(vt.Trim()));
            }

            // ── SCONTI ──
            var sconti = righe.Where(r => r.Tipo == TipoRiga.Sconto).ToList();
            if (sconti.Count > 0)
            {
                bool ok = await rt.SubtotaleAsync();
                foreach (var s in sconti)
                {
                    if (!ok) break;
                    ok = s.ScontoPercentualeCent.HasValue
                        ? await rt.ScontoPercentualeTotaleAsync(s.ScontoPercentualeCent.Value / 100m)
                        : await rt.ScontovaloreTotaleAsync(-s.PrezzoCentesimi);
                }
                if (!ok)
                    return await AnnullaEdEsci(rt, "ERRORE SCONTI", "Il registratore ha rifiutato lo sconto.");
            }

            // ── APPENDICE ELIMINACODE (comanda da banco), come il PC: annunciata prima della chiusura ──
            if (eliminacode > 0)
            {
                try { if (!await rt.PreannunciaAppendiceAsync(eliminacode)) LogManager.Warning("Appendice eliminacode non accettata"); }
                catch (Exception ex) { LogManager.Warning($"Appendice eliminacode (non critico): {ex.Message}"); }
            }

            // ── CHIUSURA ──
            // Importo esatto e parte carta del misto: chiusura "a saldo" calcolata dall'RT,
            // così un centesimo di arrotondamento sugli sconti % non lascia lo scontrino aperto.
            _fase = 2;
            bool chiusuraOk = pag.Metodo switch
            {
                MetodiPagamento.Misto => await rt.ChiudiMistoAsync(contantiCent, 0),
                MetodiPagamento.Contanti when pag.VersatoCent > totaleCent => await rt.ChiudiContantiAsync(pag.VersatoCent),
                MetodiPagamento.Contanti => await rt.ChiudiContantiAutoAsync(),
                _ => await rt.ChiudiCartaCreditoAsync()
            };

            if (!chiusuraOk)
            {
                // Chiusura inviata ma senza conferma (rifiuto o risposta persa): NON si annulla in automatico,
                // altrimenti uno scontrino magari già stampato verrebbe rifatto (doppio scontrino).
                // Decide l'operatore; se non è uscito, alla prossima vendita il controllo iniziale sblocca l'RT.
                string err = rt.UltimoErrore;
                LogManager.Error($"RT: chiusura senza conferma ({err})");
                _ultimaVenditaFallita = true;
                rt.Disconnect();
                return new EsitoVendita
                {
                    Stato = StatoEsito.Incerto,
                    Titolo = "VERIFICARE LO SCONTRINO",
                    Messaggio = "Il registratore non ha confermato la chiusura dello scontrino." +
                                (string.IsNullOrEmpty(err) ? "" : $"\n⚠️ {err}") +
                                "\n\nControllare il registratore.",
                    DaRegistrare = _ultimoDaRegistrare,
                    RestoCent = restoFinale
                };
            }

            _fase = 3;
            var footer = await rt.LeggiTxFooterAsync();
            if (footer.Confermato)
                LogManager.Info($"✅ SCONTRINO CONFERMATO: Doc={footer.NumeroDocumento}, Azz={footer.NumeroAzzeramento}");
            else
            {
                LogManager.Warning($"⚠️ TX_FOOTER non confermato: {footer.Errore ?? "risposta assente"}");
                // nessuna conferma: se lo scontrino è ancora APERTO la chiusura non è avvenuta → si annulla e la vendita resta a video
                bool aperto = false;
                try { aperto = await rt.HaScontrinoApertoAsync(); }
                catch (Exception ex) { LogManager.Warning($"Verifica scontrino aperto: {ex.Message}"); }
                if (aperto)
                {
                    LogManager.Error("RT: dopo la chiusura lo scontrino risulta ancora aperto");
                    return await AnnullaEdEsci(rt, "SCONTRINO NON CHIUSO",
                        "Il registratore non ha chiuso lo scontrino (carta, coperchio o pagamento rifiutato).\n\nLo scontrino è stato annullato: la vendita resta a video, riprova.");
                }
            }

            if (eliminacode > 0)
            {
                try { await rt.StampaAppendiceAsync(eliminacode); }
                catch (Exception ex) { LogManager.Warning($"Stampa appendice eliminacode (non critico): {ex.Message}"); }
            }

            if (cfg.ApriCassetto)
            {
                // scontrino già chiuso: un errore del cassetto non deve far perdere il numero documento letto
                try { await rt.ApriCassettoAsync(); }
                catch (Exception ex) { LogManager.Warning($"Apertura cassetto non riuscita: {ex.Message}"); }
            }

            rt.Disconnect();
            _ultimaVenditaFallita = !footer.Confermato;   // senza conferma, la prossima vendita controlla prima l'RT

            var esito = RegistraEsito(_ultimoDaRegistrare, footer, StatoEsito.Riuscito);
            esito.RestoCent = restoFinale;
            return esito;
        }

        /// <summary>
        /// Scontrino uscito (confermato dall'RT o dall'operatore): salva la vendita nell'archivio locale.
        /// </summary>
        private EsitoVendita RegistraEsito(VenditaRegistrata v, RisultatoChiusuraRT footer, StatoEsito stato)
        {
            var esito = new EsitoVendita { Stato = stato, Titolo = "SCONTRINO EMESSO" };
            if (v == null) return esito;
            if (footer != null && footer.Confermato)
            {
                v.NumeroScontrino = esito.NumeroDocumento = footer.NumeroDocumento;
                v.NumeroAzzeramento = esito.NumeroAzzeramento = footer.NumeroAzzeramento;
                v.DataEmissioneRT = DataEmissione(footer);
            }
            v.DataOra = DateTime.Now;
            try
            {
                _archivio.Salva(v);
            }
            catch (Exception ex)
            {
                esito.Stato = StatoEsito.RiuscitoSenzaArchivio;
                esito.Messaggio = $"Scontrino emesso, ma la vendita non è stata salvata in archivio:\n{ex.Message}\n\nI dati sono stati scritti nel log.";
                // come per il conto: i dati della vendita restano almeno nel log, per ricostruirla
                LogManager.Error($"=== VENDITA NON SALVATA IN ARCHIVIO === Doc {v.NumeroScontrino} Azz {v.NumeroAzzeramento} {v.DataOra:yyyy-MM-dd HH:mm:ss}\n" +
                                 string.Join("\n", (v.Righe ?? new List<RigaScontrino>()).Select(r =>
                                     $"  {r.Quantita:0.###} x {r.Descrizione} €{r.PrezzoCentesimi / 100m:F2} rep{r.RepartoFiscale}")) +
                                 $"\nErrore: {ex}");
            }
            return esito;
        }

        /// <summary>
        /// Stato Incerto: l'operatore conferma che lo scontrino è uscito → si registra la vendita
        /// (senza numero documento, che non è stato letto).
        /// </summary>
        public EsitoVendita ConfermaScontrinoUscito(EsitoVendita incerto)
        {
            // resta il controllo dell'RT alla prossima vendita: se lo scontrino fosse ancora aperto non ci si aggiungono righe
            _ultimaVenditaFallita = true;
            LogManager.Warning("Scontrino confermato a mano dall'operatore dopo comunicazione interrotta");
            return RegistraEsito(incerto?.DaRegistrare, null, StatoEsito.Riuscito);
        }

        /// <summary>Recovery dello scontrino rimasto aperto sull'RT e messaggio d'errore.</summary>
        private async Task<EsitoVendita> AnnullaEdEsci(IFiscalConnector rt, string titolo, string messaggio)
        {
            string errore = rt.UltimoErrore;
            bool recuperato = await rt.RecuperaScontrinoApertoAsync();
            _ultimaVenditaFallita = !recuperato;
            rt.Disconnect();

            return EsitoVendita.Errore(titolo,
                messaggio + "\n\n" +
                (string.IsNullOrEmpty(errore) ? "" : $"⚠️ {errore}\n\n") +
                (recuperato
                    ? "Lo scontrino è stato annullato sull'RT."
                    : "⚠️ NON È STATO POSSIBILE ANNULLARE LO SCONTRINO SULL'RT.\nUsare 'FUNZIONI CASSA → RESET RT' o riavviare il registratore."));
        }

        private static string DataEmissione(RisultatoChiusuraRT f)
        {
            // I connettori normalizzano DataEmissione a "yyyy-MM-dd" e OraEmissione a "HHmm"
            if (f == null || !f.Confermato || string.IsNullOrEmpty(f.DataEmissione) || f.DataEmissione.Length != 10)
                return null;
            string ora = f.OraEmissione ?? "";
            return ora.Length >= 4 ? $"{f.DataEmissione} {ora.Substring(0, 2)}:{ora.Substring(2, 2)}" : f.DataEmissione;
        }
    }
}
