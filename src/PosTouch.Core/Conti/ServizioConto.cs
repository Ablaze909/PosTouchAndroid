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
        private readonly Func<Impostazioni> _impostazioni;
        private readonly ArchivioVendite _archivio;
        private int _inCorso;

        public ServizioConto(Func<Impostazioni> impostazioni, ArchivioVendite archivio)
        {
            _impostazioni = impostazioni;
            _archivio = archivio;
        }

        public bool InCorso => Volatile.Read(ref _inCorso) == 1;

        /// <param name="salaId">Sala del conto (Banco = <see cref="ConfigStampe.SalaBanco"/>): decide la stampante PRECONTI.</param>
        /// <param name="eliminacode">Comanda da banco: numero stampato sul conto (0 = nessuno).</param>
        public async Task<EsitoConto> StampaAsync(List<RigaScontrino> righe, int salaId = ConfigStampe.SalaBanco, int eliminacode = 0)
        {
            if (Interlocked.CompareExchange(ref _inCorso, 1, 0) != 0)
                return EsitoConto.Errore("ATTENDERE", "Stampa in corso...\nAttendere il completamento.");
            bool preso = false;
            string ipPreso = null; int portaPresa = 0;
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

                var pre = cfg.StampantePreconti(salaId);
                if (!pre.Assegnata)
                    return EsitoConto.Errore("STAMPANTE NON IMPOSTATA", "Nessuna stampante PRECONTI impostata.\n\nImpostala in IMPOSTAZIONI → 🖨️ STAMPE.");
                preso = await AccessoStampanti.PrendiAsync(pre.IP, pre.Porta, TimeSpan.FromSeconds(10));
                ipPreso = pre.IP; portaPresa = pre.Porta;
                if (!preso) return EsitoConto.Errore("STAMPANTE OCCUPATA", "La stampante sta eseguendo un'altra stampa.\nRiprovare tra qualche secondo.");

                using var st = new StampanteTermica(pre.IP, pre.Porta);
                if (!await st.ConnettiAsync())
                    return EsitoConto.Errore("STAMPANTE NON RAGGIUNGIBILE",
                        $"{st.UltimoErrore}\n\nIl conto NON è stato stampato.\nLo scontrino resta a video: verifica che la stampante sia accesa e in rete, poi riprova.");
                if (pre.ControlloStato && await st.ControllaStatoAsync() is string problema)
                    return EsitoConto.Errore("STAMPANTE NON PRONTA", $"{problema}\n\nIl conto NON è stato stampato.\nLo scontrino resta a video: sistema la stampante e riprova.");

                // Numero preso solo a stampante collegata: se non si collega il numero non va perso
                int numero = _archivio.ProssimoNumeroConto();
                var quando = DateTime.Now;
                var dati = Finitura.Applica(LayoutConto.Crea(righe, numero, cfg.IntestazioneConto1, cfg.IntestazioneConto2, cfg.IntestazioneConto3,
                                            cfg.AccorpaRigheConto, quando, stampaNumero: cfg.StampaNumeroConto, varianti: cfg.VariantiSuConto, eliminacode: eliminacode,
                                            coperti: cfg.StampaMediaPerCoperto ? Coperti(righe, cfg.Sale?.SalaDa(salaId)?.CopertoProdottoId ?? 0) : 0),
                                            pre, cfg.Sale.CicalinoComande);
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
                return EsitoConto.Errore("ERRORE STAMPA CONTO", PosTouch.Core.Diagnostica.ErroriRete.Testo(ex));
            }
            finally
            {
                if (preso) AccessoStampanti.Rilascia(ipPreso, portaPresa);
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
            => await ProvaAsync(new ParametriStampa { IP = ip, Porta = porta }, intestazione, 0);

        /// <summary>🖨️ Stampa di prova con i parametri di una stampa (Configura): salto righe, taglio, cicalino, inversione.</summary>
        public static async Task<(bool ok, string messaggio)> ProvaAsync(ParametriStampa p, string intestazione, int tipoCicalino)
        {
            if (p == null || !p.Assegnata) return (false, "Nessuna stampante scelta.");
            var dati = Finitura.Applica(LayoutConto.Prova(intestazione, p.IP, p.Porta), p, tipoCicalino);
            string errore = await AccessoStampanti.InviaAsync(p.IP, p.Porta, dati, TimeSpan.FromSeconds(5));
            return errore == null ? (true, $"Stampa di prova inviata a {p.Descrizione}.") : (false, errore);
        }

        /// <summary>Coperti = quantità della riga dell'articolo coperto della sala (0 = nessuno).</summary>
        private static int Coperti(IEnumerable<RigaScontrino> righe, int copertoId) =>
            copertoId <= 0 ? 0 : (int)righe.Where(r => r.Tipo == TipoRiga.Prodotto && r.ProdottoId == copertoId).Sum(r => r.Quantita);
    }
}
