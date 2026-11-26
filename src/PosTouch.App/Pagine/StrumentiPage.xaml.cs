namespace PosTouch.App.Pagine
{
    public partial class StrumentiPage : ContentPage
    {
        public StrumentiPage()
        {
            InitializeComponent();
        }

        private async void OnBackOffice(object sender, EventArgs e) => await Navigation.PushAsync(new CatalogoPage());
        private async void OnImpostazioni(object sender, EventArgs e) => await Navigation.PushAsync(new ImpostazioniPage());
        private async void OnChiudi(object sender, EventArgs e) => await Navigation.PopAsync();
    }
}
