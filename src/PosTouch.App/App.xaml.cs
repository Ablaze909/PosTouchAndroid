using PosTouch.App.Pagine;
using PosTouch.App.Servizi;

namespace PosTouch.App
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            ContestoCassa.Avvia(FileSystem.AppDataDirectory);
            // errori su thread in background che chiudono l'app: almeno restano nel log
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                PosTouch.Core.Diagnostica.LogManager.Error($"ERRORE FATALE: {e.ExceptionObject}");
        }

        protected override Window CreateWindow(IActivationState activationState)
        {
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
