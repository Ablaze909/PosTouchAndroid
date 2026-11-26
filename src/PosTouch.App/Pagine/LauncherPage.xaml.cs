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

        /// <summary>Le carte si ridimensionano con lo schermo (riferimento 1280×800 come il PC).</summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width <= 0 || height <= 0) return;
            double scala = Math.Clamp(Math.Min(width / 1280, height / 800), 0.7, 1.4);
            foreach (var carta in GrigliaTasti.Children.OfType<Border>())
            {
                carta.WidthRequest = 210 * scala;
                carta.HeightRequest = 150 * scala;
                carta.Margin = new Thickness(10 * scala);
            }
            GrigliaTasti.Margin = new Thickness(0, 0, 60 * scala, 0);
            Titolo.Margin = new Thickness(80 * scala, 0, 0, 0);
            NomePuntoVendita.FontSize = 52 * scala;
            SottotitoloPuntoVendita.FontSize = 22 * scala;
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

        private async void OnVendita(object sender, TappedEventArgs e) => await Navigazione.Apri(this, () => ContestoCassa.PaginaVendita);

        private async void OnBackOffice(object sender, TappedEventArgs e)
        {
            if (await Accesso.VerificaPinAsync("BACK OFFICE")) await Navigazione.Apri(this, () => new BackOfficePage());
        }

        private async void OnStatistiche(object sender, TappedEventArgs e)
            => await MessaggioBox.Info("📊 STATISTICHE", "Le statistiche arriveranno nella prossima versione.\n\nGli incassi del giorno sono in FUNZIONI CASSA.");

        private async void OnDocumenti(object sender, TappedEventArgs e)
            => await MessaggioBox.Info("📄 DOCUMENTI", "L'elenco documenti (ristampe, annulli, resi) arriverà nella prossima versione.");

        private async void OnFunzioniCassa(object sender, TappedEventArgs e) => await Navigazione.ApriModale(this, () => new FunzioniCassaPage());

        private async void OnImpostazioni(object sender, TappedEventArgs e)
        {
            if (await Accesso.VerificaPinAsync("IMPOSTAZIONI")) await Navigazione.Apri(this, () => new ImpostazioniPage());
        }

        protected override bool OnBackButtonPressed()
        {
            OnEsci(this, null);
            return true;
        }

        private async void OnEsci(object sender, TappedEventArgs e)
        {
            if (await MessaggioBox.Domanda("ESCI", "Chiudere POS Touch?"))
                Application.Current?.Quit();
        }
    }
}
