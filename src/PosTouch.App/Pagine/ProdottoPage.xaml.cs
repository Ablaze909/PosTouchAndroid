using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    /// <summary>Copia di ProdottoDialogTouch del PC (campi della fase 1).</summary>
    public partial class ProdottoPage : ContentPage
    {
        private readonly Prodotto _p;
        private readonly List<Categoria> _categorie;

        /// <param name="esistente">null = nuovo prodotto</param>
        /// <param name="categoriaId">categoria proposta per un nuovo prodotto</param>
        public ProdottoPage(Prodotto esistente, int categoriaId)
        {
            InitializeComponent();
            _categorie = ContestoCassa.Catalogo.TutteCategorie.ToList();
            var catProposta = _categorie.FirstOrDefault(c => c.Id == categoriaId);

            _p = esistente == null
                ? new Prodotto { CategoriaId = categoriaId, Colore = catProposta?.Colore ?? "#4CAF50", ColoreTesto = "#FFFFFF", DimensioneFontTasto = 12 }
                : new Prodotto
                {
                    Id = esistente.Id, Codice = esistente.Codice, Barcode = esistente.Barcode, Descrizione = esistente.Descrizione,
                    PrezzoCentesimi = esistente.PrezzoCentesimi, CostoCentesimi = esistente.CostoCentesimi, CategoriaId = esistente.CategoriaId,
                    Colore = esistente.Colore, ColoreTesto = esistente.ColoreTesto, DimensioneFontTasto = esistente.DimensioneFontTasto,
                    IsPreferito = esistente.IsPreferito, Attivo = esistente.Attivo
                };

            Titolo.Text = esistente == null ? "➕ NUOVO PRODOTTO" : "✏️ MODIFICA PRODOTTO";
            PannelloAttivo.IsVisible = BtnElimina.IsVisible = esistente != null;
            SwAttivo.IsToggled = _p.Attivo;

            SceltaCategoria.ItemsSource = _categorie;
            SceltaCategoria.SelectedItem = _categorie.FirstOrDefault(c => c.Id == _p.CategoriaId);
            CampoDescrizione.Text = _p.Descrizione;
            CampoBarcode.Text = _p.Barcode;
            ChkPreferito.IsChecked = _p.IsPreferito;
            Aggiorna();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (string.IsNullOrEmpty(_p.Descrizione)) CampoDescrizione.Focus();
        }

        private void OnAnteprima(object sender, EventArgs e) => Aggiorna();

        private void Aggiorna()
        {
            bool haPrezzo = _p.PrezzoCentesimi > 0 || _p.Id > 0;
            TestoPrezzo.Text = haPrezzo ? $"{_p.PrezzoEuro:N2}" : "Tocca per digitare";
            TestoPrezzo.TextColor = Ui.C(haPrezzo ? "#111111" : "#BDBDBD");
            TestoCosto.Text = _p.CostoCentesimi > 0 ? $"{_p.CostoEuro:N2}" : "Per margini";
            TestoCosto.TextColor = Ui.C(_p.CostoCentesimi > 0 ? "#111111" : "#BDBDBD");
            TestoMargine.Text = _p.CostoCentesimi > 0
                ? $"💎 Margine: € {_p.MargineEuro:N2}  ({(_p.PrezzoCentesimi > 0 ? _p.MargineEuro / _p.PrezzoEuro * 100 : 0):0}%)"
                : "💎 Margine: -- €";

            TestoColore.Text = _p.Colore;
            AnteprimaColore.BackgroundColor = Ui.ColoreSicuro(_p.Colore, "#4CAF50");

            Evidenzia(BtnBianco, _p.ColoreTesto == "#FFFFFF");
            Evidenzia(BtnNero, _p.ColoreTesto == "#000000");
            Evidenzia(BtnGiallo, _p.ColoreTesto == "#FFFF00");
            Evidenzia(BtnAltro, _p.ColoreTesto is not ("#FFFFFF" or "#000000" or "#FFFF00"));
            Evidenzia(BtnFont10, _p.DimensioneFontTasto == 10);
            Evidenzia(BtnFont12, _p.DimensioneFontTasto == 12);
            Evidenzia(BtnFont14, _p.DimensioneFontTasto == 14);
            Evidenzia(BtnFont16, _p.DimensioneFontTasto == 16);

            var testo = Ui.ColoreSicuro(_p.ColoreTesto, "#FFFFFF");
            Anteprima.BackgroundColor = Ui.ColoreSicuro(_p.Colore, "#4CAF50");
            AnteprimaNome.TextColor = AnteprimaPrezzo.TextColor = testo;
            AnteprimaNome.FontSize = _p.DimensioneFontTasto;
            AnteprimaNome.Text = string.IsNullOrWhiteSpace(CampoDescrizione.Text) ? "PRODOTTO" : CampoDescrizione.Text.ToUpperInvariant();
            AnteprimaPrezzo.Text = $"€ {_p.PrezzoEuro:F2}".Replace('.', ',');
        }

        private static void Evidenzia(Button b, bool attivo)
        {
            b.BackgroundColor = Ui.C(attivo ? "#E3F2FD" : "#F5F5F5");
            b.BorderColor = Ui.C(attivo ? "#1976D2" : "#E0E0E0");
        }

        private void OnAttivoCambiato(object sender, ToggledEventArgs e)
        {
            TestoAttivo.Text = e.Value ? "ATTIVO" : "NASCOSTO";
            TestoAttivo.TextColor = Ui.C(e.Value ? "#4CAF50" : "#F44336");
        }

        private async void OnPrezzo(object sender, TappedEventArgs e)
        {
            var v = await TastierinoPrezzo.ChiediEuro("💶 PREZZO VENDITA", _p.PrezzoCentesimi > 0 ? _p.PrezzoEuro : null);
            if (v == null) return;
            _p.PrezzoCentesimi = (int)Math.Round(v.Value * 100, MidpointRounding.AwayFromZero);
            Aggiorna();
        }

        private async void OnCosto(object sender, TappedEventArgs e)
        {
            var v = await TastierinoPrezzo.ChiediEuro("💰 COSTO ACQUISTO", _p.CostoCentesimi > 0 ? _p.CostoEuro : null);
            if (v == null) return;
            _p.CostoCentesimi = (int)Math.Round(v.Value * 100, MidpointRounding.AwayFromZero);
            Aggiorna();
        }

        private async void OnTastieraBarcode(object sender, EventArgs e)
        {
            string b = await TastierinoPrezzo.ChiediCodice("📦 BARCODE / EAN", CampoBarcode.Text);
            if (b != null) CampoBarcode.Text = b;
        }

        private async void OnColore(object sender, TappedEventArgs e)
        {
            int i = await SceltaBox.Colori("Colore bottone", SceltaBox.Tavolozza);
            if (i < 0) return;
            _p.Colore = SceltaBox.Tavolozza[i];
            Aggiorna();
        }

        private void OnColoreTestoBianco(object sender, EventArgs e) { _p.ColoreTesto = "#FFFFFF"; Aggiorna(); }
        private void OnColoreTestoNero(object sender, EventArgs e) { _p.ColoreTesto = "#000000"; Aggiorna(); }
        private void OnColoreTestoGiallo(object sender, EventArgs e) { _p.ColoreTesto = "#FFFF00"; Aggiorna(); }

        private async void OnColoreTestoAltro(object sender, EventArgs e)
        {
            int i = await SceltaBox.Colori("Colore testo tasto", SceltaBox.Tavolozza);
            if (i < 0) return;
            _p.ColoreTesto = SceltaBox.Tavolozza[i];
            Aggiorna();
        }

        private void OnFont10(object sender, EventArgs e) { _p.DimensioneFontTasto = 10; Aggiorna(); }
        private void OnFont12(object sender, EventArgs e) { _p.DimensioneFontTasto = 12; Aggiorna(); }
        private void OnFont14(object sender, EventArgs e) { _p.DimensioneFontTasto = 14; Aggiorna(); }
        private void OnFont16(object sender, EventArgs e) { _p.DimensioneFontTasto = 16; Aggiorna(); }

        private async void OnSalva(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CampoDescrizione.Text))
            {
                await MessaggioBox.Avviso("CAMPO OBBLIGATORIO", "Inserire la descrizione.");
                return;
            }
            if (SceltaCategoria.SelectedItem is not Categoria cat)
            {
                await MessaggioBox.Avviso("CAMPO OBBLIGATORIO", "Scegliere la categoria.");
                return;
            }
            if (_p.PrezzoCentesimi == 0 && !await MessaggioBox.Domanda("PREZZO ZERO",
                    "Il prezzo è € 0,00.\n\nIn vendita verrà chiesto il prezzo ad ogni battitura. Continuare?"))
                return;

            _p.Descrizione = CampoDescrizione.Text.Trim().ToUpperInvariant();
            _p.CategoriaId = cat.Id;
            _p.Barcode = (CampoBarcode.Text ?? "").Trim();
            _p.IsPreferito = ChkPreferito.IsChecked;
            _p.Attivo = SwAttivo.IsToggled || _p.Id == 0;
            try
            {
                await Task.Run(() => ContestoCassa.GestioneCatalogo.SalvaProdotto(_p));
                await Navigazione.Chiudi(this);
            }
            catch (Exception ex)
            {
                await MessaggioBox.Errore("ERRORE", ex.Message);
            }
        }

        private async void OnAnnulla(object sender, EventArgs e) => await Navigazione.Chiudi(this);

        private async void OnElimina(object sender, EventArgs e)
        {
            if (!await MessaggioBox.Conferma("🗑️ ELIMINA PRODOTTO", $"Eliminare definitivamente {_p.Descrizione}?\n\nQuesta operazione non può essere annullata.")) return;
            try
            {
                await Task.Run(() => ContestoCassa.GestioneCatalogo.EliminaProdotto(_p.Id));
                await Navigazione.Chiudi(this);
            }
            catch (Exception ex)
            {
                await MessaggioBox.Errore("ERRORE", ex.Message);
            }
        }
    }
}
