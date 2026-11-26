using PosTouch.Core.Cassa;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Dati;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Fiscale;
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
        public static ArchivioVendite Vendite { get; private set; }
        public static ServizioVendita ServizioVendita { get; private set; }
        public static FunzioniCassa FunzioniCassa { get; private set; }
        public static Carrello Carrello { get; } = new Carrello();

        public static void Avvia(string cartellaDati)
        {
            if (CartellaDati != null) return;
            CartellaDati = cartellaDati;
            LogManager.Inizializza(cartellaDati);
            LogManager.Info($"=== Avvio POS Touch Android {AppInfo.Current.VersionString} (build {AppInfo.Current.BuildString}) ===");

            ArchivioImpostazioni = new ArchivioImpostazioni(cartellaDati);
            Impostazioni = ArchivioImpostazioni.Carica();
            ImpostazioniFiscali.Correnti = Impostazioni.ComeImpostazioniFiscali();

            Catalogo = new Catalogo(cartellaDati);
            try { Catalogo.Carica(); }
            catch (Exception ex) { LogManager.Error($"Catalogo non caricato: {ex.Message}"); }

            Vendite = new ArchivioVendite(cartellaDati);
            ServizioVendita = new ServizioVendita(() => Impostazioni, Vendite);
            FunzioniCassa = new FunzioniCassa(() => Impostazioni);
        }

        public static void SalvaImpostazioni(Impostazioni nuove)
        {
            ArchivioImpostazioni.Salva(nuove);
            Impostazioni = nuove;
        }
    }
}
