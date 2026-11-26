using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;

namespace PosTouch.App.Pagine
{
    /// <summary>Copia di StrumentiDialog del PC.</summary>
    public partial class StrumentiPage : PaginaPos
    {
        public StrumentiPage()
        {
            InitializeComponent();
        }

        // con la gestione operatori ogni voce chiede il suo permesso (senza: la password è già stata chiesta per entrare)
        private async void OnBackOffice(object sender, TappedEventArgs e)
        {
            if (await Accesso.VerificaAsync(PosTouch.Core.Modelli.Permesso.BackOffice, "BACK OFFICE")) await Navigazione.Apri(this, () => new BackOfficePage());
        }
        private async void OnImpostazioni(object sender, TappedEventArgs e)
        {
            if (await Accesso.VerificaAsync(PosTouch.Core.Modelli.Permesso.Impostazioni, "IMPOSTAZIONI")) await Navigazione.Apri(this, () => new ImpostazioniPage());
        }
        private async void OnStatistiche(object sender, TappedEventArgs e) => await ProssimaVersione("📊 STATISTICHE");
        private async void OnDocumenti(object sender, TappedEventArgs e) => await ProssimaVersione("📄 DOCUMENTI");
        private async void OnDatabase(object sender, TappedEventArgs e) => await ProssimaVersione("🗄️ DATABASE");
        private async void OnContiClienti(object sender, TappedEventArgs e) => await ProssimaVersione("👥 CONTI CLIENTI");
        private async void OnChiudi(object sender, EventArgs e) => await Navigazione.Chiudi(this);

        private static Task ProssimaVersione(string titolo) => MessaggioBox.Info(titolo, "Funzione disponibile nella prossima versione.");
    }
}
