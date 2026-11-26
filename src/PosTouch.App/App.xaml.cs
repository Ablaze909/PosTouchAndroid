using PosTouch.App.Pagine;
using PosTouch.App.Servizi;

namespace PosTouch.App
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            // prima dell'avvio: anche un errore all'apertura dei database finisce nel log
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                PosTouch.Core.Diagnostica.LogManager.Error($"ERRORE FATALE: {e.ExceptionObject}");
            try { ContestoCassa.Avvia(FileSystem.AppDataDirectory); }
            catch (Exception ex)
            {
                // database danneggiato o memoria piena: invece di chiudersi a ogni apertura, l'app mostra il motivo
                _erroreAvvio = ex;
                try { PosTouch.Core.Diagnostica.LogManager.Error($"AVVIO NON RIUSCITO: {ex}"); PosTouch.Core.Diagnostica.LogManager.Svuota(); } catch { }
            }
        }

        private readonly Exception _erroreAvvio;

        private static Page PaginaErroreAvvio(Exception ex) => new ContentPage
        {
            BackgroundColor = Color.FromArgb("#263238"),
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = 40, Spacing = 16,
                    Children =
                    {
                        new Label { Text = "⚠️ POS TOUCH NON PUÒ AVVIARSI", FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                        new Label { Text = "Non è stato possibile aprire i dati della cassa (database danneggiato o memoria del tablet piena).\n\n" +
                                           "Non disinstallare l'app: si perderebbero vendite e tavoli. Contatta l'assistenza con questo messaggio.",
                                    FontSize = 18, TextColor = Color.FromArgb("#ECEFF1") },
                        new Label { Text = ex.GetType().Name + ": " + ex.Message, FontSize = 14, TextColor = Color.FromArgb("#FFB74D") },
                        new Label { Text = "Cartella dati: " + FileSystem.AppDataDirectory, FontSize = 12, TextColor = Color.FromArgb("#90A4AE") }
                    }
                }
            }
        };

        protected override Window CreateWindow(IActivationState activationState)
        {
            if (_erroreAvvio != null) return new Window(PaginaErroreAvvio(_erroreAvvio)) { Title = "POS Touch" };
            var nav = new NavigationPage(new LauncherPage())
            {
                BarBackgroundColor = Color.FromArgb("#1E3A5F"),
                BarTextColor = Colors.White
            };
            var finestra = new Window(nav) { Title = "POS Touch" };
            // app in background: tavolo e log salvati subito (Android potrebbe chiuderla senza avvisare)
            finestra.Stopped += (s, e) =>
            {
                ContestoCassa.PaginaVenditaAperta?.SalvaBozzaSubito();
                PosTouch.Core.Diagnostica.LogManager.Svuota();
            };
            return finestra;
        }
    }
}
