using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    public partial class CategoriaPage : ContentPage
    {
        private static readonly int[] Aliquote = { 22, 10, 5, 4, 0 };
        private readonly Categoria _cat;

        /// <param name="esistente">null = nuova categoria</param>
        public CategoriaPage(Categoria esistente)
        {
            InitializeComponent();
            _cat = esistente == null
                ? new Categoria { RepartoFiscale = 1, AliquotaIVA = 22, ColoreTesto = "#FFFFFF", Ordine = ContestoCassa.Catalogo.Categorie.Count + 1 }
                : new Categoria
                {
                    Id = esistente.Id, Nome = esistente.Nome, RepartoFiscale = esistente.RepartoFiscale,
                    AliquotaIVA = esistente.AliquotaIVA, Colore = esistente.Colore, ColoreTesto = esistente.ColoreTesto,
                    Ordine = esistente.Ordine
                };

            Titolo.Text = esistente == null ? "➕ NUOVA CATEGORIA" : "✏️ MODIFICA CATEGORIA";
            BtnElimina.IsVisible = esistente != null;

            CampoNome.Text = _cat.Nome;
            CampoReparto.Text = _cat.RepartoFiscale.ToString();
            int iva = Array.IndexOf(Aliquote, _cat.AliquotaIVA);
            SceltaIva.SelectedIndex = iva >= 0 ? iva : 0;
            CampoOrdine.Text = _cat.Ordine.ToString();
            Colore.Colore = _cat.Colore;
            SceltaTesto.SelectedIndex = string.Equals(_cat.ColoreTesto, "#000000", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            Colore.Cambiato += AggiornaAnteprima;
            AggiornaAnteprima();
        }

        private string ColoreTesto => SceltaTesto.SelectedIndex == 1 ? "#000000" : "#FFFFFF";

        private void OnAnteprima(object sender, EventArgs e) => AggiornaAnteprima();

        private void AggiornaAnteprima()
        {
            Anteprima.BackgroundColor = Color.TryParse(Colore.Colore, out var c) ? c : Colors.Gray;
            AnteprimaTesto.TextColor = Color.FromArgb(ColoreTesto);
            AnteprimaTesto.Text = string.IsNullOrWhiteSpace(CampoNome.Text) ? "CATEGORIA" : CampoNome.Text.ToUpperInvariant();
        }

        private async void OnSalva(object sender, EventArgs e)
        {
            if (!int.TryParse(CampoReparto.Text, out int reparto) || reparto < 1 || reparto > 99)
            {
                await DisplayAlert("Categoria", "Reparto RT non valido (1-99).", "OK");
                return;
            }
            int.TryParse(CampoOrdine.Text, out int ordine);

            _cat.Nome = (CampoNome.Text ?? "").Trim().ToUpperInvariant();
            _cat.RepartoFiscale = reparto;
            _cat.AliquotaIVA = Aliquote[Math.Max(0, SceltaIva.SelectedIndex)];
            _cat.Ordine = ordine;
            _cat.Colore = Colore.Colore;
            _cat.ColoreTesto = ColoreTesto;
            try
            {
                await Task.Run(() => ContestoCassa.GestioneCatalogo.SalvaCategoria(_cat));
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Categoria", ex.Message, "OK");
            }
        }

        private async void OnAnnulla(object sender, EventArgs e) => await Navigation.PopAsync();

        private async void OnElimina(object sender, EventArgs e)
        {
            if (!await DisplayAlert("Elimina", $"Eliminare la categoria {_cat.Nome}?", "Elimina", "Annulla")) return;
            try
            {
                await Task.Run(() => ContestoCassa.GestioneCatalogo.EliminaCategoria(_cat.Id));
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Categoria", ex.Message, "OK");
            }
        }
    }
}
