using PosTouch.Core.Cassa;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Dati;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Fiscale;
using PosTouch.Core.Sale;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Servizi
{
    /// <summary>
    /// Punto unico da cui le pagine prendono i servizi della cassa.
    /// Creato una volta all'avvio dell'app.
    /// </summary>
    public static class ContestoCassa
    {
        public static string CartellaDati { get; private set; }
        public static Impostazioni Impostazioni { get; private set; }
        public static ArchivioImpostazioni ArchivioImpostazioni { get; private set; }
        public static Catalogo Catalogo { get; private set; }
        public static GestioneCatalogo GestioneCatalogo { get; private set; }
        public static ArchivioVendite Vendite { get; private set; }
        public static ServizioVendita ServizioVendita { get; private set; }
        public static PosTouch.Core.Conti.ServizioConto ServizioConto { get; private set; }
        public static FunzioniCassa FunzioniCassa { get; private set; }
        public static Carrello Carrello { get; } = new Carrello();
        public static ArchivioTavoli Tavoli { get; private set; }
        public static SpoolerComande Spooler { get; private set; }
        /// <summary>Comanda da banco con eliminacode (scontrino e conto al banco).</summary>
        public static ComandaBanco ComandaBanco { get; private set; }

        /// <summary>Apre un tavolo (lo crea se libero) con le impostazioni correnti.</summary>
        public static SessioneTavolo ApriTavolo(int salaId, int numero, string nomeCliente = null)
            => new SessioneTavolo(Tavoli, Spooler, () => Impostazioni, salaId, numero, nomeCliente);

        private static PosTouch.App.Pagine.VenditaPage _paginaVendita;
        /// <summary>Una sola schermata vendita per tutta l'app (lo scontrino in corso è uno solo).</summary>
        public static PosTouch.App.Pagine.VenditaPage PaginaVendita => _paginaVendita ??= new PosTouch.App.Pagine.VenditaPage();
        /// <summary>La vendita se è già stata aperta (null altrimenti): per salvare quando l'app va in background.</summary>
        public static PosTouch.App.Pagine.VenditaPage PaginaVenditaAperta => _paginaVendita;

        /// <summary>Aumenta a ogni salvataggio impostazioni: la vendita si ridisegna solo se serve.</summary>
        public static int VersioneImpostazioni { get; private set; }

        public static void Avvia(string cartellaDati)
        {
            if (CartellaDati != null) return;
            CartellaDati = cartellaDati;
            LogManager.Inizializza(cartellaDati);
            LogManager.Info($"=== Avvio POS Touch Android {AppInfo.Current.VersionString} (build {AppInfo.Current.BuildString}) ===");

            ArchivioImpostazioni = new ArchivioImpostazioni(cartellaDati);
            Impostazioni = ArchivioImpostazioni.Carica();
            ImpostazioniFiscali.Correnti = Impostazioni.ComeImpostazioniFiscali();
            LogManager.SetLivelloMinimo(Impostazioni.LivelloLog);

            Catalogo = new Catalogo(cartellaDati);
            try { Catalogo.Carica(); }
            catch (Exception ex) { LogManager.Error($"Catalogo non caricato: {ex.Message}"); }

            GestioneCatalogo = new GestioneCatalogo(Catalogo);
            Carrello.RisolviProdotto = id => Catalogo.ProdottoDa(id);   // ricalcolo combinazioni

            Vendite = new ArchivioVendite(cartellaDati);
            ServizioVendita = new ServizioVendita(() => Impostazioni, Vendite);
            ServizioConto = new PosTouch.Core.Conti.ServizioConto(() => Impostazioni, Vendite);
            FunzioniCassa = new FunzioniCassa(() => Impostazioni);

            Tavoli = new ArchivioTavoli(cartellaDati);
            Spooler = new SpoolerComande(Tavoli);
            Spooler.StampaInErrore += job => MainThread.BeginInvokeOnMainThread(async () =>
                await PosTouch.App.Dialoghi.MessaggioBox.Avviso("🖨️ STAMPA NON RIUSCITA",
                    $"{job.Descrizione}\n\nStampante {job.Stampante} ({job.IP}:{job.Porta}) non raggiungibile:\n{job.UltimoErrore}\n\n" +
                    "La stampa resta in CODA e viene ritentata ogni 30 secondi.\nControlla la stampante o apri 🖨️ CODA."));
            Spooler.Avvia();
            ComandaBanco = new ComandaBanco(Tavoli, Spooler, () => Impostazioni, () => Catalogo);
        }

        public static void SalvaImpostazioni(Impostazioni nuove)
        {
            ArchivioImpostazioni.Salva(nuove);
            Impostazioni = nuove;
            VersioneImpostazioni++;
        }
    }
}
