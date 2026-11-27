using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
using PosTouch.App.Servizi;

namespace PosTouch.App
{
    [Activity(Theme = "@style/Maui.SplashTheme",
              MainLauncher = true,
              LaunchMode = LaunchMode.SingleTop,
              ScreenOrientation = ScreenOrientation.SensorLandscape,
              ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                                     ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density |
                                     ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
    public class MainActivity : MauiAppCompatActivity
    {
        /// <summary>
        /// Ignora la "dimensione testo" impostata in Android: le schermate mantengono
        /// le proporzioni del PC su qualsiasi tablet.
        /// </summary>
        protected override void AttachBaseContext(Context @base)
        {
            var config = new Android.Content.Res.Configuration(@base.Resources.Configuration) { FontScale = 1f };
            base.AttachBaseContext(@base.CreateConfigurationContext(config));
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            ProteggiDaErrori();   // prima di tutto: anche un errore nella creazione della prima schermata è coperto
            base.OnCreate(savedInstanceState);
            // Cassa: lo schermo non deve spegnersi durante il servizio
            Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
            SchermoIntero();

            // Quando si chiude la tastiera a schermo Android rimostra le barre: le rinascondiamo
            if (Window?.DecorView?.ViewTreeObserver is ViewTreeObserver osservatore)
                osservatore.GlobalLayout += (s, e) => ControllaBarre();
        }

        private static bool _protetto;

        /// <summary>
        /// Rete di sicurezza: un errore imprevisto in un tasto non deve chiudere la cassa.
        /// Viene scritto nel log e mostrato all'operatore; lo scontrino a video resta com'è.
        /// </summary>
        private static void ProteggiDaErrori()
        {
            if (_protetto) return;
            _protetto = true;
            Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (s, e) =>
            {
                PosTouch.Core.Diagnostica.LogManager.Error($"ERRORE IMPREVISTO: {e.Exception}");
                e.Handled = true;
                try
                {
                    Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
                        await PosTouch.App.Dialoghi.MessaggioBox.Errore("ERRORE IMPREVISTO",
                            "Si è verificato un errore imprevisto (registrato nel log).\n\n" + e.Exception.Message));
                }
                catch { }
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                PosTouch.Core.Diagnostica.LogManager.Error($"Errore in attività di fondo: {e.Exception}");
                e.SetObserved();
            };
        }

        protected override void OnResume()
        {
            base.OnResume();
            SchermoInteroRitardato();
        }

        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);
            // il focus torna dopo menu a tendina, finestre di sistema, selettore file…
            if (hasFocus) SchermoInteroRitardato();
        }

        private bool _inAttesa;
        private long _ultimoControllo;

        /// <summary>Se le barre sono visibili e la tastiera a schermo è chiusa, torna a schermo intero.</summary>
        private void ControllaBarre()
        {
            if (_inAttesa || Window?.DecorView == null) return;
            // scatta a ogni ridisegno (anche l'orologio ogni secondo): al massimo un controllo ogni 300 ms
            long ora = Android.OS.SystemClock.UptimeMillis();
            if (ora - _ultimoControllo < 300) return;
            _ultimoControllo = ora;
            using var insets = ViewCompat.GetRootWindowInsets(Window.DecorView);
            if (insets == null) return;
            bool tastiera = insets.IsVisible(WindowInsetsCompat.Type.Ime());
            bool barre = insets.IsVisible(WindowInsetsCompat.Type.SystemBars());
            if (barre && !tastiera) SchermoInteroRitardato();
        }

        private void SchermoInteroRitardato()
        {
            if (_inAttesa || Window?.DecorView == null) return;
            _inAttesa = true;
            Window.DecorView.PostDelayed(() =>
            {
                _inAttesa = false;
                SchermoIntero();
            }, 300);
        }

        /// <summary>
        /// A tutto schermo come il PC (finestra senza bordi): nasconde barra di stato e tasti di sistema.
        /// Si richiamano scorrendo dal bordo e poi si nascondono da soli.
        /// </summary>
        private void SchermoIntero()
        {
            if (Window == null) return;
            var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
            if (controller == null) return;
            controller.Hide(WindowInsetsCompat.Type.SystemBars());
            controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        }

        /// <summary>Tasti da tastiere fisiche (lettore barcode) inviati al LettoreBarcode quando la vendita è aperta.</summary>
        public override bool DispatchKeyEvent(KeyEvent e)
        {
            if (e != null && LettoreBarcode.Attivo && e.Device != null && !e.Device.IsVirtual)
            {
                bool invio = e.KeyCode == Keycode.Enter || e.KeyCode == Keycode.NumpadEnter;
                if (e.Action == KeyEventActions.Down)
                {
                    char c = (char)e.UnicodeChar;
                    if (LettoreBarcode.Tasto(c, invio)) return true;
                }
                else if (e.Action == KeyEventActions.Up && (invio || LettoreBarcode.Valido((char)e.UnicodeChar)))
                {
                    return true; // consumo anche il rilascio dei tasti già usati
                }
            }
            return base.DispatchKeyEvent(e);
        }
    }
}
