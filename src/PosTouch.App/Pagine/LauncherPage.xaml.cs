using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;

namespace PosTouch.App.Pagine
{
    /// <summary>Schermata iniziale, come LauncherDialog del PC.</summary>
    public partial class LauncherPage : PaginaPos
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
            if (!_deviazioniControllate) { _deviazioniControllate = true; Dispatcher.Dispatch(async () => await ControllaDeviazioniAsync()); }
        }

        private static bool _deviazioniControllate;

        /// <summary>ControllaDeviazioniSalvate del PC: all'avvio chiede se le stampanti deviate funzionano di nuovo.</summary>
        private static async Task ControllaDeviazioniAsync()
        {
            try
            {
                var dev = ContestoCassa.Spooler.Deviazioni();
                if (dev.Count == 0) return;
                string lista = string.Join("\n", dev.Select(d => $"• {d.NomeOrigine} → {d.NomeDestinazione} (dal {d.Attivata:dd/MM HH:mm})"));
                bool ok = await MessaggioBox.Domanda("🔀 DEVIAZIONI STAMPANTI — CONTROLLO AVVIO",
                    $"Nella sessione precedente erano attive queste deviazioni:\n\n{lista}\n\nLe stampanti funzionano correttamente?\n\n" +
                    "• SÌ → tolgo le deviazioni (stampa normale)\n• NO → le lascio attive");
                if (ok) ContestoCassa.Spooler.RimuoviDeviazioni();
            }
            catch (Exception ex) { PosTouch.Core.Diagnostica.LogManager.Error($"Controllo deviazioni: {ex.Message}"); }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _orologio.Stop();
        }

        private void AggiornaOra() => OraData.Text = DateTime.Now.ToString("dddd dd/MM/yyyy  HH:mm");

        private async void OnVendita(object sender, TappedEventArgs e)
        {
            // gestione operatori: PIN all'apertura del turno (una volta), poi si vende
            if (!await Turni.AssicuraTurnoAsync()) return;
            await Navigazione.Apri(this, () => ContestoCassa.PaginaVendita);
        }

        private async void OnBackOffice(object sender, TappedEventArgs e)
        {
            if (await Accesso.VerificaPinAsync("BACK OFFICE", PosTouch.Core.Modelli.Permesso.BackOffice)) await Navigazione.Apri(this, () => new BackOfficePage());
        }

        private async void OnStatistiche(object sender, TappedEventArgs e)
            => await MessaggioBox.Info("📊 STATISTICHE", "Le statistiche arriveranno nella prossima versione.\n\nGli incassi del giorno sono in FUNZIONI CASSA.");

        private async void OnDocumenti(object sender, TappedEventArgs e)
            => await MessaggioBox.Info("📄 DOCUMENTI", "L'elenco documenti (ristampe, annulli, resi) arriverà nella prossima versione.");

        private async void OnFunzioniCassa(object sender, TappedEventArgs e) => await FunzioniCassaBox.Mostra();

        private async void OnImpostazioni(object sender, TappedEventArgs e)
        {
            if (await Accesso.VerificaPinAsync("IMPOSTAZIONI")) await Navigazione.Apri(this, () => new ImpostazioniPage());
        }

        protected override bool Indietro()
        {
            OnEsci(this, null);
            return true;
        }

        private async void OnEsci(object sender, TappedEventArgs e)
        {
            if (!await MessaggioBox.Domanda("ESCI", "Chiudere POS Touch?")) return;
            // uscita dalla porta: si riordinano gli archivi (niente viene cancellato), al massimo qualche secondo.
            // Se l'app si chiude in un altro modo questa volta si salta, e si rifà alla prossima uscita.
            try
            {
                await AttesaBox.Durante("OTTIMIZZAZIONE ARCHIVI", "Un momento, l'app si chiude da sola…", async () =>
                {
                    var lavoro = Task.Run(() =>
                    {
                        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();   // rilascia i collegamenti rimasti aperti
                        PosTouch.Core.Dati.Compattazione.Esegui(ContestoCassa.CartellaDati);
                    });
                    await Task.WhenAny(lavoro, Task.Delay(TimeSpan.FromSeconds(60)));   // mai bloccati sull'uscita
                });
            }
            catch (Exception ex) { PosTouch.Core.Diagnostica.LogManager.Error($"Ottimizzazione all'uscita: {ex.Message}"); }
            PosTouch.Core.Diagnostica.LogManager.Svuota();
            Application.Current?.Quit();
        }
    }
}
