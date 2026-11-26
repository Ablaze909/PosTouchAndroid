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
        }

        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);
            if (hasFocus) SchermoIntero();
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
