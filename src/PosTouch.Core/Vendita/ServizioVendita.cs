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
        Fallito
    }

    public class EsitoVendita
    {
        public StatoEsito Stato { get; set; }
        public string Titolo { get; set; } = "";
        public string Messaggio { get; set; } = "";
        public int RestoCent { get; set; }
        public int NumeroDocumento { get; set; }
        public int NumeroAzzeramento { get; set; }

        public bool Emesso => Stato != StatoEsito.Fallito;

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
        private bool _txFooterConfigurato;

        public ServizioVendita(Func<Impostazioni> impostazioni, ArchivioVendite archivio)
        {
            _impostazioni = impostazioni;
            _archivio = archivio;
        }

        public bool InCorso => Volatile.Read(ref _inCorso) == 1;

        public async Task<EsitoVendita> EmettiAsync(List<RigaScontrino> righe, Pagamento pagamento)
        {
            if (Interlocked.CompareExchange(ref _inCorso, 1, 0) != 0)
                return EsitoVendita.Errore("ATTENDERE", "Vendita in corso, attendere il completamento.");
            try
            {
                return await EmettiInternoAsync(righe, pagamento);
            }
            catch (Exception ex)
            {
                LogManager.Error($"Vendita: errore imprevisto: {ex}");
                _ultimaVenditaFallita = true;
                return EsitoVendita.Errore("ERRORE", $"Errore imprevisto durante la vendita:\n{ex.Message}\n\nControllare il registratore prima di riprovare.");
            }
            finally
            {
                Interlocked.Exchange(ref _inCorso, 0);
            }
        }

        private async Task<EsitoVendita> EmettiInternoAsync(List<RigaScontrino> righe, Pagamento pag)
        {
            var cfg = _impostazioni();
            righe = righe.Where(r => r.ContaNelTotale).ToList(); // subtotale e separatori sono solo visivi
            var prodotti = righe.Where(r => r.Tipo == TipoRiga.Prodotto).ToList();
            int totaleCent = righe.Sum(r => r.ImportoCentesimi);

            // ── VALIDAZIONE ──
            if (prodotti.Count == 0)
                return EsitoVendita.Errore("ATTENZIONE", "Lo scontrino è vuoto.");
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
                        "1. Verificare coperchio e carta\n2. Usare 'Funzioni cassa → Sblocca RT'\n3. Se persiste, spegnere e riaccendere il registratore");
                }
                _ultimaVenditaFallita = false;
            }

            if (!_txFooterConfigurato)
            {
                await rt.ConfiguraTxFooterJsonAsync();
                _txFooterConfigurato = true;
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
            foreach (var riga in daInviare)
            {
                LogManager.Debug($"Riga: Rep{riga.RepartoFiscale} {riga.Descrizione} €{riga.PrezzoCentesimi / 100.0:F2} x{riga.Quantita}");
                if (!await rt.VendiRepartoAsync(riga.RepartoFiscale, riga.Descrizione, riga.PrezzoCentesimi, riga.Quantita))
                {
                    LogManager.Error($"RT ha RIFIUTATO riga: {riga.Descrizione}");
                    return await AnnullaEdEsci(rt, "ERRORE REGISTRATORE",
                        "Il registratore ha rifiutato un articolo.\n\n" +
                        "Verificare coperchio, carta e reparti/aliquote configurati sull'RT.");
                }
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

            // ── CHIUSURA ──
            bool chiusuraOk = pag.Metodo switch
            {
                MetodiPagamento.Misto => await rt.ChiudiMistoAsync(contantiCent, cartaCent),
                MetodiPagamento.Contanti => await rt.ChiudiContantiAsync(Math.Max(pag.VersatoCent, totaleCent)),
                _ => await rt.ChiudiCartaCreditoAsync()
            };

            if (!chiusuraOk)
            {
                LogManager.Error("RT ha rifiutato la chiusura scontrino");
                return await AnnullaEdEsci(rt, "ERRORE CHIUSURA", "Il registratore ha rifiutato la chiusura dello scontrino.");
            }

            var footer = await rt.LeggiTxFooterAsync();
            if (footer.Confermato)
                LogManager.Info($"✅ SCONTRINO CONFERMATO: Doc={footer.NumeroDocumento}, Azz={footer.NumeroAzzeramento}");
            else
                LogManager.Warning($"⚠️ TX_FOOTER non confermato: {footer.Errore ?? "risposta assente"}");

            if (cfg.ApriCassetto)
                await rt.ApriCassettoAsync();

            rt.Disconnect();
            _ultimaVenditaFallita = false;

            // ── SALVATAGGIO LOCALE ──
            var esito = new EsitoVendita
            {
                Stato = StatoEsito.Riuscito,
                Titolo = "SCONTRINO EMESSO",
                RestoCent = restoCent,
                NumeroDocumento = footer.Confermato ? footer.NumeroDocumento : 0,
                NumeroAzzeramento = footer.Confermato ? footer.NumeroAzzeramento : 0
            };

            try
            {
                _archivio.Salva(new VenditaRegistrata
                {
                    NumeroScontrino = esito.NumeroDocumento,
                    NumeroAzzeramento = esito.NumeroAzzeramento,
                    TotaleCentesimi = totaleCent,
                    MetodoPagamento = pag.Metodo,
                    ContantiCent = contantiCent,
                    CartaCent = cartaCent,
                    StampatoSuRT = true,
                    DataEmissioneRT = DataEmissione(footer),
                    Righe = righe
                });
            }
            catch (Exception ex)
            {
                esito.Stato = StatoEsito.RiuscitoSenzaArchivio;
                esito.Messaggio = $"Scontrino emesso, ma la vendita non è stata salvata in archivio:\n{ex.Message}";
            }
            return esito;
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
                    : "⚠️ NON È STATO POSSIBILE ANNULLARE LO SCONTRINO SULL'RT.\nUsare 'Funzioni cassa → Sblocca RT' o riavviare il registratore."));
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
