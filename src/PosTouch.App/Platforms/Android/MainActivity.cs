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
                                     ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
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
            base.OnCreate(savedInstanceState);
            // Cassa: lo schermo non deve spegnersi durante il servizio
            Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
            SchermoIntero();

            // Quando si chiude la tastiera a schermo Android rimostra le barre: le rinascondiamo
            if (Window?.DecorView?.ViewTreeObserver is ViewTreeObserver osservatore)
                osservatore.GlobalLayout += (s, e) => ControllaBarre();
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

        /// <summary>Se le barre sono visibili e la tastiera a schermo è chiusa, torna a schermo intero.</summary>
        private void ControllaBarre()
        {
            if (_inAttesa || Window?.DecorView == null) return;
            var insets = ViewCompat.GetRootWindowInsets(Window.DecorView);
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
                else if (e.Action == KeyEventActions.Up && (invio || char.IsLetterOrDigit((char)e.UnicodeChar)))
                {
                    return true; // consumo anche il rilascio dei tasti già usati
                }
            }
            return base.DispatchKeyEvent(e);
        }
    }
}
