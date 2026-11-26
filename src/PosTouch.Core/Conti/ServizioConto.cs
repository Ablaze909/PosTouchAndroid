using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Dati;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;
using PosTouch.Core.Stampa;

namespace PosTouch.Core.Conti
{
    public enum StatoConto
    {
        /// <summary>Stampato e salvato.</summary>
        Stampato,
        /// <summary>Stampato ma non salvato in archivio (errore database).</summary>
        StampatoSenzaArchivio,
        /// <summary>Non è uscito nulla (stampante non raggiungibile o dati non validi): lo scontrino resta a video.</summary>
        NonStampato,
        /// <summary>Connessione caduta durante l'invio: decide l'operatore guardando la stampante.</summary>
        Incerto
    }

    public class EsitoConto
    {
        public StatoConto Stato { get; set; }
        public string Titolo { get; set; } = "";
        public string Messaggio { get; set; } = "";
        public int Numero { get; set; }
        internal List<RigaScontrino> Righe { get; set; }
        internal DateTime Quando { get; set; }

        internal static EsitoConto Errore(string titolo, string messaggio) =>
            new EsitoConto { Stato = StatoConto.NonStampato, Titolo = titolo, Messaggio = messaggio };
    }

    /// <summary>
    /// Tasto CONTO (btnConto_Click del PC): conto non fiscale sulla termica in rete,
    /// numero progressivo e salvataggio nello storico (tabelle Conti/ContiRighe).
    /// </summary>
    public class ServizioConto
    {
        private static readonly SemaphoreSlim _termica = new SemaphoreSlim(1, 1);

        private readonly Func<Impostazioni> _impostazioni;
        private readonly ArchivioVendite _archivio;
        private int _inCorso;

        public ServizioConto(Func<Impostazioni> impostazioni, ArchivioVendite archivio)
        {
            _impostazioni = impostazioni;
            _archivio = archivio;
        }

        public bool InCorso => Volatile.Read(ref _inCorso) == 1;

        public async Task<EsitoConto> StampaAsync(List<RigaScontrino> righe)
        {
            if (Interlocked.CompareExchange(ref _inCorso, 1, 0) != 0)
                return EsitoConto.Errore("ATTENDERE", "Stampa in corso...\nAttendere il completamento.");
            bool preso = false;
            try
            {
                var cfg = _impostazioni();
                if (!cfg.AbilitaStampanteTermica)
                    return EsitoConto.Errore("FUNZIONE DISABILITATA",
                        "La stampante termica è disabilitata nelle impostazioni.\n\nAbilitala per poter stampare conti.");
                if (!righe.Any(r => r.Tipo == TipoRiga.Prodotto))
                    return EsitoConto.Errore("ATTENZIONE", "Lo scontrino è vuoto!\n\nAggiungi almeno un prodotto prima di stampare il conto.");
                int totale = righe.Where(r => r.ContaNelTotale).Sum(r => r.ImportoCentesimi);
                if (totale <= 0)
                    return EsitoConto.Errore("TOTALE NON VALIDO", "Non è possibile stampare un conto con totale negativo o zero!");

                preso = await _termica.WaitAsync(TimeSpan.FromSeconds(10));
                if (!preso) return EsitoConto.Errore("STAMPANTE OCCUPATA", "La stampante sta eseguendo un'altra stampa.\nRiprovare tra qualche secondo.");

                using var st = new StampanteTermica(cfg.IP_Termica, cfg.Porta_Termica);
                if (!await st.ConnettiAsync())
                    return EsitoConto.Errore("STAMPANTE NON RAGGIUNGIBILE",
                        $"{st.UltimoErrore}\n\nIl conto NON è stato stampato.\nLo scontrino resta a video: verifica che la stampante sia accesa e in rete, poi riprova.");

                // Numero preso solo a stampante collegata: se non si collega il numero non va perso
                int numero = _archivio.ProssimoNumeroConto();
                var quando = DateTime.Now;
                var dati = LayoutConto.Crea(righe, numero, cfg.IntestazioneConto1, cfg.IntestazioneConto2, cfg.IntestazioneConto3,
                                            cfg.AccorpaRigheConto, quando, stampaNumero: cfg.StampaNumeroConto);
                try
                {
                    await st.InviaAsync(dati);
                }
                catch (Exception ex)
                {
                    LogManager.Error($"Conto N. {numero}: invio interrotto: {ex.Message}");
                    return new EsitoConto
                    {
                        Stato = StatoConto.Incerto, Numero = numero, Righe = righe, Quando = quando,
                        Titolo = "VERIFICARE IL CONTO",
                        Messaggio = $"La comunicazione con la stampante si è interrotta durante la stampa del conto N. {numero}.\n\n" +
                                    $"Totale: € {totale / 100m:N2}"
                    };
                }
                LogManager.Info($"Conto N. {numero} stampato: €{totale / 100m:F2}");
                return Registra(righe, numero, quando, true);
            }
            catch (Exception ex)
            {
                LogManager.Error($"Conto: errore imprevisto: {ex}");
                return EsitoConto.Errore("ERRORE STAMPA CONTO", ex.Message);
            }
            finally
            {
                if (preso) _termica.Release();
                Interlocked.Exchange(ref _inCorso, 0);
            }
        }

        /// <summary>Stato Incerto: l'operatore conferma che il conto è uscito → si salva con lo stesso numero.</summary>
        public EsitoConto ConfermaContoStampato(EsitoConto incerto)
        {
            if (incerto?.Righe == null) return incerto;
            LogManager.Warning($"Conto N. {incerto.Numero} confermato a mano dall'operatore");
            return Registra(incerto.Righe, incerto.Numero, incerto.Quando, true);
        }

        private EsitoConto Registra(List<RigaScontrino> righe, int numero, DateTime quando, bool stampato)
        {
            try
            {
                _archivio.SalvaConto(righe, numero, quando, stampato);
                return new EsitoConto { Stato = StatoConto.Stampato, Numero = numero, Titolo = "CONTO STAMPATO" };
            }
            catch (Exception ex)
            {
                // Rete di sicurezza: il conto resta almeno nel log
                LogManager.Error($"=== CONTO NON SALVATO IN ARCHIVIO === N. {numero} {quando:yyyy-MM-dd HH:mm:ss}\n" +
                                 string.Join("\n", righe.Where(r => r.ContaNelTotale).Select(r => $"  {r.Quantita}x {r.Descrizione} €{r.ImportoEuro:F2}")));
                return new EsitoConto
                {
                    Stato = StatoConto.StampatoSenzaArchivio, Numero = numero, Titolo = "⚠️ CONTO NON SALVATO",
                    Messaggio = $"Il conto N. {numero} è stato STAMPATO\nma NON è stato salvato nello storico.\n\nErrore: {ex.Message}\n\nI dati sono stati scritti nel log."
                };
            }
        }

        /// <summary>🖨️ Stampa di prova (impostazioni), con IP e porta indicati.</summary>
        public static async Task<(bool ok, string messaggio)> ProvaAsync(string ip, int porta, string intestazione)
        {
            if (!await _termica.WaitAsync(TimeSpan.FromSeconds(5))) return (false, "La stampante sta eseguendo un'altra stampa.");
            try
            {
                using var st = new StampanteTermica(ip, porta);
                if (!await st.ConnettiAsync()) return (false, st.UltimoErrore);
                await st.InviaAsync(LayoutConto.Prova(intestazione, ip, porta));
                return (true, $"Stampa di prova inviata a {ip}:{porta}.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
            finally
            {
                _termica.Release();
            }
        }
    }
}
