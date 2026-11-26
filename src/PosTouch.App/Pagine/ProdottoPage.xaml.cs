using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    /// <summary>Copia di ProdottoDialogTouch del PC (campi della fase 1).</summary>
    public partial class ProdottoPage : PaginaPos
    {
        private readonly Prodotto _p;
        private readonly List<Categoria> _categorie;

        /// <param name="esistente">null = nuovo prodotto</param>
        /// <param name="categoriaId">categoria proposta per un nuovo prodotto</param>
        private List<PosTouch.Core.Configurazione.CentroProduzione> _centri = new();

        private static readonly string[] Unita = { "Kg", "Etto", "g" };
        private static readonly int[] Gusti = { 2, 3, 4, 5, 6 };
        private static readonly List<string> Politiche = new() { "Prezzo del gusto più caro", "Media dei gusti", "Prezzo fisso dell'articolo" };

        // ══════════════ tipo articolo (uno solo: standard / coperto / a peso / combinazione) ══════════════

        private enum TipoArticolo { Standard, Coperto, Peso, Combinazione }
        private TipoArticolo _tipo;

        private void OnTipoStandard(object s, EventArgs e) => ImpostaTipo(TipoArticolo.Standard);
        private void OnTipoCoperto(object s, EventArgs e) => ImpostaTipo(TipoArticolo.Coperto);
        private void OnTipoPeso(object s, EventArgs e) => ImpostaTipo(TipoArticolo.Peso);
        private void OnTipoCombinazione(object s, EventArgs e) => ImpostaTipo(TipoArticolo.Combinazione);

        /// <summary>Accende il tasto del tipo scelto e mostra le sue opzioni (unità di peso / gusti e prezzo).</summary>
        private void ImpostaTipo(TipoArticolo t)
        {
            _tipo = t;
            void Colora(Button b, TipoArticolo x, string colore)
            {
                bool on = t == x;
                b.BackgroundColor = Color.FromArgb(on ? colore : "#ECEFF1");
                b.TextColor = Color.FromArgb(on ? "#FFFFFF" : "#546E7A");
            }
            Colora(BtnTipoStandard, TipoArticolo.Standard, "#546E7A");
            Colora(BtnTipoCoperto, TipoArticolo.Coperto, "#00897B");
            Colora(BtnTipoPeso, TipoArticolo.Peso, "#6D4C41");
            Colora(BtnTipoCombinazione, TipoArticolo.Combinazione, "#E65100");
            PannelloPeso.IsVisible = t == TipoArticolo.Peso;
            PannelloCombinazione.IsVisible = t == TipoArticolo.Combinazione;
            AiutoTipo.Text = t switch
            {
                TipoArticolo.Coperto => "Si sceglie come coperto della sala (SALE E COMANDE): viene chiesto all'apertura del tavolo.",
                TipoArticolo.Peso => "In vendita chiede il peso; il prezzo è per unità di peso.",
                TipoArticolo.Combinazione => "In vendita si scelgono i gusti (es. pizza ½ + ½); il prezzo segue la regola scelta.",
                _ => "Articolo normale."
            };
        }

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
                    IsPreferito = esistente.IsPreferito, Attivo = esistente.Attivo, CentroProduzioneId = esistente.CentroProduzioneId,
                    VenditaAPeso = esistente.VenditaAPeso, UnitaPeso = esistente.UnitaPeso, IsCodiceBilancia = esistente.IsCodiceBilancia,
                    IsCombinazione = esistente.IsCombinazione, NumeroGusti = esistente.NumeroGusti, PoliticaPrezzo = esistente.PoliticaPrezzo,
                    VariantiProporzionali = esistente.VariantiProporzionali, IsCoperto = esistente.IsCoperto
                };

            Titolo.Text = esistente == null ? "➕ NUOVO PRODOTTO" : "✏️ MODIFICA PRODOTTO";
            PannelloAttivo.IsVisible = BtnElimina.IsVisible = esistente != null;
            SwAttivo.IsToggled = _p.Attivo;

            // tipo articolo
            ImpostaTipo(_p.IsCombinazione ? TipoArticolo.Combinazione : _p.VenditaAPeso ? TipoArticolo.Peso : _p.IsCoperto ? TipoArticolo.Coperto : TipoArticolo.Standard);
            SceltaUnita.Voci = Unita;
            SceltaUnita.Indice = Math.Max(0, Array.IndexOf(Unita, _p.UnitaPeso));
            ChkBilancia.IsChecked = _p.IsCodiceBilancia;
            SceltaGusti.Voci = Gusti.Select(g => $"{g} gusti").ToList();
            SceltaGusti.Indice = Math.Max(0, Array.IndexOf(Gusti, _p.NumeroGusti));
            SceltaPolitica.Voci = Politiche;
            SceltaPolitica.Indice = Math.Clamp(_p.PoliticaPrezzo, 0, 2);
            ChkProporzionali.IsChecked = _p.VariantiProporzionali;

            // centro comande: come la categoria / nessuna stampa / centro specifico
            _centri = ContestoCassa.Impostazioni.Sale.Centri.ToList();
            SceltaCentro.Voci = new[] { "📂 Come la categoria", "🚫 Nessuna stampa" }.Concat(_centri.Select(c => c.Nome)).ToList();
            int ic = _centri.FindIndex(c => c.Id == _p.CentroProduzioneId);
            SceltaCentro.Indice = _p.CentroProduzioneId < 0 ? 1 : ic >= 0 ? ic + 2 : 0;
            SceltaCategoria.Voci = _categorie.Select(c => c.Nome).ToList();
            SceltaCategoria.Indice = _categorie.FindIndex(c => c.Id == _p.CategoriaId);
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

        private bool _salvataggio;

        /// <summary>SALVA protetto dal doppio tocco (un secondo INSERT creerebbe un duplicato).</summary>
        private async void OnSalva(object sender, EventArgs e)
        {
            if (_salvataggio) return;
            _salvataggio = true;
            try { await SalvaAsync(); }
            finally { _salvataggio = false; }
        }

        private async Task SalvaAsync()
        {
            if (string.IsNullOrWhiteSpace(CampoDescrizione.Text))
            {
                await MessaggioBox.Avviso("CAMPO OBBLIGATORIO", "Inserire la descrizione.");
                return;
            }
            var cat = SceltaCategoria.Indice >= 0 ? _categorie[SceltaCategoria.Indice] : null;
            if (cat == null)
            {
                await MessaggioBox.Avviso("CAMPO OBBLIGATORIO", "Scegliere la categoria.");
                return;
            }
            if (_p.PrezzoCentesimi == 0 && !await MessaggioBox.Domanda("PREZZO ZERO",
                    "Il prezzo è € 0,00.\n\nIn vendita verrà chiesto il prezzo ad ogni battitura. Continuare?"))
                return;

            _p.VenditaAPeso = _tipo == TipoArticolo.Peso;
            _p.IsCoperto = _tipo == TipoArticolo.Coperto;
            _p.UnitaPeso = Unita[Math.Max(0, SceltaUnita.Indice)];
            _p.IsCodiceBilancia = ChkBilancia.IsChecked;
            _p.IsCombinazione = _tipo == TipoArticolo.Combinazione;
            _p.NumeroGusti = Gusti[Math.Max(0, SceltaGusti.Indice)];
            _p.PoliticaPrezzo = Math.Max(0, SceltaPolitica.Indice);
            _p.VariantiProporzionali = ChkProporzionali.IsChecked;
            _p.CentroProduzioneId = SceltaCentro.Indice == 1 ? -1 : SceltaCentro.Indice >= 2 ? _centri[SceltaCentro.Indice - 2].Id : 0;
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
