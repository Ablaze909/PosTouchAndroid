using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    /// <summary>Elenco categorie (con reparto RT) e articoli, con accesso alla modifica.</summary>
    public partial class CatalogoPage : ContentPage
    {
        private Categoria _categoria;

        public CatalogoPage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Ricarica();
        }

        private void Ricarica()
        {
            int id = _categoria?.Id ?? 0;
            var categorie = ContestoCassa.Catalogo.Categorie.ToList();
            ListaCategorie.ItemsSource = categorie;
            _categoria = categorie.FirstOrDefault(c => c.Id == id) ?? categorie.FirstOrDefault();
            ListaCategorie.SelectedItem = _categoria;
            MostraArticoli();
        }

        private void MostraArticoli()
        {
            BtnNuovoArticolo.IsEnabled = _categoria != null;
            if (_categoria == null)
            {
                TitoloArticoli.Text = "ARTICOLI";
                ListaArticoli.ItemsSource = null;
                return;
            }
            TitoloArticoli.Text = $"ARTICOLI — {_categoria.Nome.ToUpperInvariant()}";
            VuotoArticoli.Text = "Nessun articolo in questa categoria";
            ListaArticoli.ItemsSource = ContestoCassa.Catalogo.ProdottiDi(_categoria.Id).ToList();
        }

        private void OnCategoriaScelta(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is Categoria c)
            {
                _categoria = c;
                MostraArticoli();
            }
        }

        private async void OnArticoloScelto(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is not Prodotto p) return;
            ListaArticoli.SelectedItem = null;
            await Navigation.PushAsync(new ProdottoPage(p, p.CategoriaId));
        }

        private async void OnNuovaCategoria(object sender, EventArgs e)
            => await Navigation.PushAsync(new CategoriaPage(null));

        private async void OnModificaCategoria(object sender, EventArgs e)
        {
            if (_categoria == null) return;
            await Navigation.PushAsync(new CategoriaPage(_categoria));
        }

        private async void OnNuovoArticolo(object sender, EventArgs e)
        {
            if (_categoria == null) return;
            await Navigation.PushAsync(new ProdottoPage(null, _categoria.Id));
        }
    }
}
