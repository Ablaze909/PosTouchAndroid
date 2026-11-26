using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
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
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            // Cassa: lo schermo non deve spegnersi durante il servizio
            Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
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
