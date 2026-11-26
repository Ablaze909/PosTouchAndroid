using Microsoft.Extensions.Logging;

namespace PosTouch.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // Formati italiani ovunque (€ 1,50 - 26/11/2026) anche se il tablet è impostato in un'altra lingua
            var it = new System.Globalization.CultureInfo("it-IT");
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = it;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = it;
            System.Globalization.CultureInfo.CurrentCulture = it;
            System.Globalization.CultureInfo.CurrentUICulture = it;

            var builder = MauiApp.CreateBuilder();
            builder.UseMauiApp<App>();
#if DEBUG
            builder.Logging.AddDebug();
#endif
            return builder.Build();
        }
    }
}
