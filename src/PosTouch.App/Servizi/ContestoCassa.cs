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
        /// <summary>Operatori e turni (gestione attivabile da Impostazioni → Sicurezza).</summary>
        public static ServizioOperatori Operatori { get; private set; }

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
            // comanda: gli articoli delle categorie ⚡ SUBITO escono sempre con "!! SUBITO !!"
            PosTouch.Core.Sale.Portate.CategoriaSubito = id => Catalogo.TutteCategorie.Any(c => c.Id == id && c.Subito);
            PosTouch.Core.Sale.Portate.CategoriaFinePasto = id => Catalogo.TutteCategorie.Any(c => c.Id == id && c.FinePasto);
            try { Catalogo.Carica(); }
            catch (Exception ex) { LogManager.Error($"Catalogo non caricato: {ex.Message}"); }

            GestioneCatalogo = new GestioneCatalogo(Catalogo);
            Carrello.RisolviProdotto = id => Catalogo.ProdottoDa(id);   // ricalcolo combinazioni

            Vendite = new ArchivioVendite(cartellaDati);
            Operatori = new ServizioOperatori(new ArchivioOperatori(cartellaDati), Vendite, () => Impostazioni.AbilitaGestioneOperatori);
            // vendite, conti e stampe registrano l'operatore del turno senza doverlo passare a ogni chiamata
            Vendite.OperatoreCorrente = () => Operatori.Abilitata && Operatori.Corrente is { } o ? (o.Id, o.Nome) : ((int, string)?)null;
            PosTouch.Core.Stampa.OperatoreStampa.Nome = () => Operatori.Abilitata ? Operatori.Corrente?.Nome : null;
            ServizioVendita = new ServizioVendita(() => Impostazioni, Vendite);
            ServizioConto = new PosTouch.Core.Conti.ServizioConto(() => Impostazioni, Vendite);
            FunzioniCassa = new FunzioniCassa(() => Impostazioni);

            Tavoli = new ArchivioTavoli(cartellaDati);
            Spooler = new SpoolerComande(Tavoli);
            Spooler.StampaInErrore += job => MainThread.BeginInvokeOnMainThread(() => _ = AvvisoStampaAsync(job));
            Spooler.Avvia();
            ComandaBanco = new ComandaBanco(Tavoli, Spooler, () => Impostazioni, () => Catalogo);
        }

        /// <summary>Stampanti con l'avviso "stampa non riuscita" già a video: un avviso per stampante, non uno per comanda.</summary>
        private static readonly HashSet<string> _avvisiStampa = new HashSet<string>();

        private static async Task AvvisoStampaAsync(JobStampa job)
        {
            string chiave = $"{job.IP}:{job.Porta}";
            if (!_avvisiStampa.Add(chiave)) return;   // c'è già l'avviso per questa stampante: non se ne impilano altri
            try
            {
                int ferme = 0;
                try { ferme = Tavoli.CodaDaStampare(true).Count(j => j.IP == job.IP && j.Porta == job.Porta); } catch { }
                string altre = ferme > 1 ? $"\nIn coda per questa stampante: {ferme} stampe (escono in ordine appena torna).\n" : "\n";
                await PosTouch.App.Dialoghi.MessaggioBox.Avviso("🖨️ STAMPA NON RIUSCITA",
                    $"{job.Descrizione}\n\nStampante {job.Stampante} ({job.IP}:{job.Porta}) non raggiungibile:\n{PosTouch.Core.Diagnostica.ErroriRete.Traduci(job.UltimoErrore)}\n" + altre +
                    "\nLe stampe restano in CODA e vengono ritentate ogni 20 secondi.\nControlla la stampante o apri 🖨️ CODA (RIPROVA o DEVIA).");
            }
            finally { _avvisiStampa.Remove(chiave); }
        }

        public static void SalvaImpostazioni(Impostazioni nuove)
        {
            ArchivioImpostazioni.Salva(nuove);
            Impostazioni = nuove;
            VersioneImpostazioni++;
        }
    }
}
