using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    public partial class ProdottoPage : ContentPage
    {
        private readonly Prodotto _p;
        private readonly List<Categoria> _categorie;

        /// <param name="esistente">null = nuovo articolo</param>
        /// <param name="categoriaId">categoria proposta per un nuovo articolo</param>
        public ProdottoPage(Prodotto esistente, int categoriaId)
        {
            InitializeComponent();
            _categorie = ContestoCassa.Catalogo.Categorie.ToList();
            var catProposta = _categorie.FirstOrDefault(c => c.Id == categoriaId);

            _p = esistente == null
                ? new Prodotto { CategoriaId = categoriaId, Colore = catProposta?.Colore, ColoreTesto = "#FFFFFF" }
                : new Prodotto
                {
                    Id = esistente.Id, Codice = esistente.Codice, Barcode = esistente.Barcode, Descrizione = esistente.Descrizione,
                    PrezzoCentesimi = esistente.PrezzoCentesimi, CategoriaId = esistente.CategoriaId,
                    Colore = esistente.Colore, ColoreTesto = esistente.ColoreTesto, IsPreferito = esistente.IsPreferito
                };

            Title = esistente == null ? "Nuovo articolo" : $"Articolo {esistente.Codice}";
            BtnElimina.IsVisible = esistente != null;

            SceltaCategoria.ItemsSource = _categorie;
            SceltaCategoria.SelectedItem = _categorie.FirstOrDefault(c => c.Id == _p.CategoriaId);
            CampoDescrizione.Text = _p.Descrizione;
            CampoPrezzo.Text = esistente == null ? "" : (_p.PrezzoCentesimi / 100m).ToString("0.00");
            CampoBarcode.Text = _p.Barcode;
            SwPreferito.IsToggled = _p.IsPreferito;
            Colore.Colore = _p.Colore;
            SceltaTesto.SelectedIndex = string.Equals(_p.ColoreTesto, "#000000", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            Colore.Cambiato += AggiornaAnteprima;
            AggiornaAnteprima();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (string.IsNullOrEmpty(_p.Descrizione)) CampoDescrizione.Focus();
        }

        private string ColoreTesto => SceltaTesto.SelectedIndex == 1 ? "#000000" : "#FFFFFF";

        private void OnAnteprima(object sender, EventArgs e) => AggiornaAnteprima();

        private void AggiornaAnteprima()
        {
            Anteprima.BackgroundColor = Color.TryParse(Colore.Colore, out var c) ? c : Colors.Gray;
            var testo = Color.FromArgb(ColoreTesto);
            AnteprimaTesto.TextColor = AnteprimaPrezzo.TextColor = testo;
            AnteprimaTesto.Text = string.IsNullOrWhiteSpace(CampoDescrizione.Text) ? "ARTICOLO" : CampoDescrizione.Text.ToUpperInvariant();
            AnteprimaPrezzo.Text = Importi.ProvaCentesimi(CampoPrezzo.Text, out int cent) ? Importi.Euro(cent) : "€ 0,00";
        }

        private async void OnSalva(object sender, EventArgs e)
        {
            if (SceltaCategoria.SelectedItem is not Categoria cat)
            {
                await DisplayAlert("Articolo", "Scegliere la categoria.", "OK");
                return;
            }
            string prezzo = (CampoPrezzo.Text ?? "").Trim();
            int cent = 0;
            if (prezzo.Length > 0 && prezzo != "0" && !Importi.ProvaCentesimi(prezzo, out cent))
            {
                await DisplayAlert("Articolo", "Prezzo non valido.", "OK");
                return;
            }
            if (cent == 0 && !await DisplayAlert("Articolo",
                    "Prezzo a zero: in vendita andrà battuto il prezzo con il tasto €. Continuare?", "Sì", "No"))
                return;

            _p.Descrizione = (CampoDescrizione.Text ?? "").Trim().ToUpperInvariant();
            _p.PrezzoCentesimi = cent;
            _p.CategoriaId = cat.Id;
            _p.Barcode = (CampoBarcode.Text ?? "").Trim();
            _p.IsPreferito = SwPreferito.IsToggled;
            _p.Colore = Colore.Colore;
            _p.ColoreTesto = ColoreTesto;
            try
            {
                await Task.Run(() => ContestoCassa.GestioneCatalogo.SalvaProdotto(_p));
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Articolo", ex.Message, "OK");
            }
        }

        private async void OnElimina(object sender, EventArgs e)
        {
            if (!await DisplayAlert("Elimina", $"Eliminare l'articolo {_p.Descrizione}?", "Elimina", "Annulla")) return;
            try
            {
                await Task.Run(() => ContestoCassa.GestioneCatalogo.EliminaProdotto(_p.Id));
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Articolo", ex.Message, "OK");
            }
        }
    }
}
