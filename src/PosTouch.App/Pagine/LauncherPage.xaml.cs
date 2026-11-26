using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;

namespace PosTouch.App.Pagine
{
    /// <summary>Schermata iniziale, come LauncherDialog del PC.</summary>
    public partial class LauncherPage : ContentPage
    {
        private readonly IDispatcherTimer _orologio;

        public LauncherPage()
        {
            InitializeComponent();
            Versione.Text = $"POS Touch Android v{AppInfo.Current.VersionString} (build {AppInfo.Current.BuildString})";
            _orologio = Dispatcher.CreateTimer();
            _orologio.Interval = TimeSpan.FromSeconds(1);
            _orologio.Tick += (s, e) => AggiornaOra();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            var cfg = ContestoCassa.Impostazioni;
            NomePuntoVendita.Text = string.IsNullOrWhiteSpace(cfg.NomePuntoVendita) ? "POS Touch" : cfg.NomePuntoVendita;
            SottotitoloPuntoVendita.Text = cfg.SottotitoloPuntoVendita ?? "";
            AggiornaOra();
            _orologio.Start();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _orologio.Stop();
        }

        private void AggiornaOra() => OraData.Text = DateTime.Now.ToString("dddd dd/MM/yyyy  HH:mm");

        private async void OnVendita(object sender, TappedEventArgs e) => await Navigation.PushAsync(new VenditaPage());

        private async void OnBackOffice(object sender, TappedEventArgs e)
        {
            if (await Accesso.VerificaPinAsync("BACK OFFICE")) await Navigation.PushAsync(new BackOfficePage());
        }

        private async void OnStatistiche(object sender, TappedEventArgs e)
            => await MessaggioBox.Info("📊 STATISTICHE", "Le statistiche arriveranno nella prossima versione.\n\nGli incassi del giorno sono in FUNZIONI CASSA.");

        private async void OnDocumenti(object sender, TappedEventArgs e)
            => await MessaggioBox.Info("📄 DOCUMENTI", "L'elenco documenti (ristampe, annulli, resi) arriverà nella prossima versione.");

        private async void OnFunzioniCassa(object sender, TappedEventArgs e) => await Navigation.PushAsync(new FunzioniCassaPage());

        private async void OnImpostazioni(object sender, TappedEventArgs e)
        {
            if (await Accesso.VerificaPinAsync("IMPOSTAZIONI")) await Navigation.PushAsync(new ImpostazioniPage());
        }

        private async void OnEsci(object sender, TappedEventArgs e)
        {
            if (await MessaggioBox.Domanda("ESCI", "Chiudere POS Touch?"))
                Application.Current?.Quit();
        }
    }
}
