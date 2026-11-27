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
    /// <summary>
    /// Emissione dello scontrino fiscale dal banco.
    /// Stessa sequenza collaudata di MainWindow.EseguiVenditaAsync (POS Touch v17):
    /// health check → operatore → righe → sconti → chiusura → TX_FOOTER → cassetto → salvataggio.
    /// Non mostra nulla a video: restituisce un <see cref="EsitoVendita"/> che la UI visualizza.
    /// </summary>
    public partial class ServizioVendita
    {
        private readonly Func<Impostazioni> _impostazioni;
        private readonly ArchivioVendite _archivio;

        private int _inCorso;                 // 1 = vendita in corso (blocca doppio tocco)
        private string _txFooterTentativo;   // chiave dell'RT che ha già rifiutato TX_FOOTER una volta
        // true anche all'avvio: se l'app si era chiusa a metà vendita, la prima vendita controlla e sblocca l'RT
        // (prima si partiva "pulito" e le righe nuove potevano finire nello scontrino rimasto aperto)
        private bool _ultimaVenditaFallita = true;   // FIX #16 originale: recovery preventivo alla vendita successiva
        private string _txFooterConfiguratoPer;  // tipo|ip|porta del registratore già configurato
        private int _fase;                       // 0 = prima delle righe, 1 = righe inviate, 2 = chiusura inviata, 3 = chiuso

        private readonly Func<Impostazioni, IFiscalConnector> _creaRT;

        /// <param name="creaRT">Solo per i test: registratore finto. Predefinito: quello delle impostazioni (3i o Epson).</param>
        public ServizioVendita(Func<Impostazioni> impostazioni, ArchivioVendite archivio, Func<Impostazioni, IFiscalConnector> creaRT = null)
        {
            _impostazioni = impostazioni;
            _archivio = archivio;
            _creaRT = creaRT ?? (c => FiscalConnectorFactory.Create(c.TipoCassa, c.IP_RT, c.Porta_RT));
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

        /// <summary>
        /// Emissione in fasi (file ServizioVendita.Emissione.cs): ogni fase restituisce null se si può proseguire,
        /// altrimenti l'esito con cui fermarsi. L'ordine è quello del protocollo e non va cambiato.
        /// </summary>
        private async Task<EsitoVendita> EmettiInternoAsync(List<RigaScontrino> righe, Pagamento pag, int eliminacode)
        {
            _fase = 0;
            _ultimoDaRegistrare = null;
            var e = new Emissione { Cfg = _impostazioni(), Pagamento = pag, Eliminacode = eliminacode };

            var stop = Valida(e, righe);
            if (stop != null) return stop;

            using var rt = _creaRT(e.Cfg);
            stop = await PreparaRTAsync(rt, e)
                ?? await InviaRigheAsync(rt, e)
                ?? await InviaScontiAsync(rt, e);
            if (stop != null) return stop;

            await PreannunciaAppendiceAsync(rt, e);
            stop = await ChiudiAsync(rt, e);
            if (stop != null) return stop;

            var (fermo, footer) = await VerificaChiusuraAsync(rt, e);
            if (fermo != null) return fermo;

            await OperazioniDopoChiusuraAsync(rt, e);
            rt.Disconnect();
            _ultimaVenditaFallita = !footer.Confermato;   // senza conferma, la prossima vendita controlla prima l'RT

            var esito = RegistraEsito(_ultimoDaRegistrare, footer, StatoEsito.Riuscito);
            esito.RestoCent = e.RestoCent;
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
