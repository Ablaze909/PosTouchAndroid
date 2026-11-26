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
        }

        protected override Window CreateWindow(IActivationState activationState)
        {
            var nav = new NavigationPage(new LauncherPage())
            {
                BarBackgroundColor = Color.FromArgb("#1E3A5F"),
                BarTextColor = Colors.White
            };
            return new Window(nav) { Title = "POS Touch" };
        }
    }
}
