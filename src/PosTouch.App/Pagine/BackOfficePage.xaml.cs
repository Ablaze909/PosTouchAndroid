using System.ComponentModel;
using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    /// <summary>Riga della lista prodotti del Back office (con spunta di selezione).</summary>
    public class VoceProdotto : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public Prodotto P { get; init; }
        private bool _selezionato;
        public bool Selezionato
        {
            get => _selezionato;
            set
            {
                _selezionato = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Spunta)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sfondo)));
            }
        }
        public string Spunta => Selezionato ? "☑" : "☐";
        public string Sfondo => Selezionato ? "#E3F2FD" : "#FFFFFF";
        public double Opacita => P.Attivo ? 1.0 : 0.5;
        public string Titolo => (P.IsPreferito ? "⭐ " : "") + P.Descrizione + (P.Attivo ? "" : "  (nascosto)");
        public string Iva => $"{P.AliquotaIVA}%";
    }

    /// <summary>Copia del Back office del PC: schede PRODOTTI e CATEGORIE.</summary>
    public partial class BackOfficePage : ContentPage
    {
        private List<VoceProdotto> _voci = new();
        private bool _soloPreferiti, _soloNascosti;
        private bool _schedaProdotti = true;

        public BackOfficePage()
        {
            InitializeComponent();
            MostraScheda(true);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Ricarica();
        }

        private void Ricarica()
        {
            var cat = ContestoCassa.Catalogo;
            Contatori.Text = $"{cat.TuttiProdotti.Count} Prodotti • {cat.TutteCategorie.Count} Categorie";

            var filtro = new List<Categoria> { new Categoria { Id = 0, Nome = "Tutte" } };
            filtro.AddRange(cat.TutteCategorie);
            int idScelto = (FiltroCategoria.SelectedItem as Categoria)?.Id ?? 0;
            FiltroCategoria.ItemsSource = filtro;
            FiltroCategoria.SelectedItem = filtro.FirstOrDefault(c => c.Id == idScelto) ?? filtro[0];

            FiltraProdotti();
            CaricaCategorie();
        }

        // ══════════════ schede ══════════════

        private void OnSchedaProdotti(object sender, TappedEventArgs e) => MostraScheda(true);
        private void OnSchedaCategorie(object sender, TappedEventArgs e) => MostraScheda(false);

        private void MostraScheda(bool prodotti)
        {
            _schedaProdotti = prodotti;
            PannelloProdotti.IsVisible = prodotti;
            PannelloCategorie.IsVisible = !prodotti;
            SchedaProdotti.BackgroundColor = Ui.C(prodotti ? "#2196F3" : "#FFFFFF");
            TestoSchedaProdotti.TextColor = Ui.C(prodotti ? "#FFFFFF" : "#333333");
            SchedaCategorie.BackgroundColor = Ui.C(prodotti ? "#FFFFFF" : "#2196F3");
            TestoSchedaCategorie.TextColor = Ui.C(prodotti ? "#333333" : "#FFFFFF");
        }

        private async void OnChiudi(object sender, EventArgs e) => await Navigation.PopAsync();

        // ══════════════ PRODOTTI ══════════════

        private void OnFiltroCambiato(object sender, EventArgs e) => FiltraProdotti();

        private void OnChipPreferiti(object sender, TappedEventArgs e)
        {
            _soloPreferiti = !_soloPreferiti;
            ChipPreferiti.Stroke = new SolidColorBrush(_soloPreferiti ? Ui.C("#F57F17") : Colors.Transparent);
            FiltraProdotti();
        }

        private void OnChipNascosti(object sender, TappedEventArgs e)
        {
            _soloNascosti = !_soloNascosti;
            ChipNascosti.Stroke = new SolidColorBrush(_soloNascosti ? Ui.C("#C62828") : Colors.Transparent);
            FiltraProdotti();
        }

        private void FiltraProdotti()
        {
            if (FiltroCategoria == null || ListaProdotti == null) return;
            int catId = (FiltroCategoria.SelectedItem as Categoria)?.Id ?? 0;
            string testo = (CampoRicerca.Text ?? "").Trim();
            string barcode = (CampoBarcode.Text ?? "").Trim();

            var selezionati = new HashSet<int>(_voci.Where(v => v.Selezionato).Select(v => v.P.Id));
            _voci = ContestoCassa.Catalogo.TuttiProdotti
                .Where(p => catId == 0 || p.CategoriaId == catId)
                .Where(p => testo.Length == 0 || p.Descrizione.Contains(testo, StringComparison.OrdinalIgnoreCase) || p.Codice.Contains(testo))
                .Where(p => barcode.Length == 0 || (p.Barcode ?? "").Contains(barcode))
                .Where(p => !_soloPreferiti || p.IsPreferito)
                .Where(p => !_soloNascosti || !p.Attivo)
                .Select(p => new VoceProdotto { P = p, Selezionato = selezionati.Contains(p.Id) })
                .ToList();
            ListaProdotti.ItemsSource = _voci;
            AggiornaBarraAzioni();
        }

        private async void OnProdottoToccato(object sender, TappedEventArgs e)
        {
            if ((sender as BindableObject)?.BindingContext is VoceProdotto v)
                await Navigation.PushAsync(new ProdottoPage(v.P, v.P.CategoriaId));
        }

        private void OnSpuntaToccata(object sender, TappedEventArgs e)
        {
            if ((sender as BindableObject)?.BindingContext is not VoceProdotto v) return;
            v.Selezionato = !v.Selezionato;
            AggiornaBarraAzioni();
        }

        private void OnSelezionaTutti(object sender, TappedEventArgs e)
        {
            bool tutti = !_voci.All(v => v.Selezionato);
            foreach (var v in _voci) v.Selezionato = tutti;
            AggiornaBarraAzioni();
        }

        private List<int> Selezionati() => _voci.Where(v => v.Selezionato).Select(v => v.P.Id).ToList();

        private void AggiornaBarraAzioni()
        {
            int n = _voci.Count(v => v.Selezionato);
            BarraAzioni.IsVisible = n > 0;
            TestoSelezionati.Text = n == 1 ? "1 selezionato" : $"{n} selezionati";
        }

        private async void OnNuovoProdotto(object sender, EventArgs e)
        {
            var categorie = ContestoCassa.Catalogo.TutteCategorie;
            if (categorie.Count == 0)
            {
                await MessaggioBox.Avviso("NESSUNA CATEGORIA", "Crea prima una categoria nella scheda 🏷️ CATEGORIE.");
                return;
            }
            int catId = (FiltroCategoria.SelectedItem as Categoria)?.Id ?? 0;
            await Navigation.PushAsync(new ProdottoPage(null, catId > 0 ? catId : categorie[0].Id));
        }

        private async void OnSposta(object sender, EventArgs e)
        {
            var categorie = ContestoCassa.Catalogo.TutteCategorie.ToList();
            int i = await SceltaBox.Testi("📂 SPOSTA IN CATEGORIA", categorie.Select(c => c.Nome).ToList());
            if (i < 0) return;
            await Esegui(() => ContestoCassa.GestioneCatalogo.SpostaProdotti(Selezionati(), categorie[i].Id));
        }

        private async void OnNascondi(object sender, EventArgs e) => await Esegui(() => ContestoCassa.GestioneCatalogo.ImpostaAttivo(Selezionati(), false));
        private async void OnMostra(object sender, EventArgs e) => await Esegui(() => ContestoCassa.GestioneCatalogo.ImpostaAttivo(Selezionati(), true));

        private async void OnElimina(object sender, EventArgs e)
        {
            var ids = Selezionati();
            if (!await MessaggioBox.Conferma("🗑️ ELIMINA", $"Eliminare definitivamente {ids.Count} prodotti?\n\nQuesta operazione non può essere annullata.")) return;
            await Esegui(() => ContestoCassa.GestioneCatalogo.EliminaProdotti(ids));
            foreach (var v in _voci) v.Selezionato = false;
        }

        private async Task Esegui(Action azione)
        {
            try
            {
                await Task.Run(azione);
                Ricarica();
            }
            catch (Exception ex)
            {
                await MessaggioBox.Errore("ERRORE", ex.Message);
            }
        }

        // ══════════════ CATEGORIE ══════════════

        private void OnFiltroCategorie(object sender, TextChangedEventArgs e) => CaricaCategorie();

        private void CaricaCategorie()
        {
            if (GrigliaCategorie == null) return;
            string testo = (CampoRicercaCategorie.Text ?? "").Trim();
            GrigliaCategorie.Children.Clear();
            foreach (var c in ContestoCassa.Catalogo.TutteCategorie.Where(c => testo.Length == 0 || c.Nome.Contains(testo, StringComparison.OrdinalIgnoreCase)))
                GrigliaCategorie.Children.Add(CreaTessera(c));
        }

        /// <summary>Tessera colorata come nel Back office del PC: ▲▼, icona, nome, "N pz · R rep", NASCOSTA.</summary>
        private View CreaTessera(Categoria c)
        {
            var testo = Ui.ColoreSicuro(c.ColoreTesto, "#000000");
            var griglia = new Grid
            {
                RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
                ColumnDefinitions = { new(GridLength.Star), new(new GridLength(36)), new(new GridLength(36)) },
                Padding = new Thickness(8)
            };
            if (!c.Attiva)
                griglia.Add(Ui.Riquadro(new Label { Text = "NASCOSTA", FontSize = 9, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                                        "#80FF0000", 4, new Thickness(4, 2)), 0, 0);
            var su = Ui.Riquadro(new Label { Text = "▲", FontSize = 12, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
                                 "#FFFFFF", 5, new Thickness(0, 4));
            var giu = Ui.Riquadro(new Label { Text = "▼", FontSize = 12, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
                                  "#FFFFFF", 5, new Thickness(0, 4));
            su.Margin = giu.Margin = new Thickness(2, 0);
            Ui.Tocco(su, async () => await Sposta(c, -1));
            Ui.Tocco(giu, async () => await Sposta(c, +1));
            griglia.Add(su, 1, 0);
            griglia.Add(giu, 2, 0);

            var centro = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
            if (!string.IsNullOrWhiteSpace(c.Icona)) centro.Add(new Label { Text = c.Icona, FontSize = 22, HorizontalOptions = LayoutOptions.Center });
            centro.Add(new Label { Text = c.Nome, FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = testo, HorizontalTextAlignment = TextAlignment.Center });
            griglia.Add(centro, 0, 1);
            Grid.SetColumnSpan(centro, 3);

            var info = Ui.Riquadro(new Label { Text = $"{c.NumeroProdotti}pz · R{c.RepartoFiscale} · {c.AliquotaIVA}%", FontSize = 12, FontAttributes = FontAttributes.Bold },
                                   "#FFFFFF", 5, new Thickness(6, 3));
            info.HorizontalOptions = LayoutOptions.Center;
            griglia.Add(info, 0, 2);
            Grid.SetColumnSpan(info, 3);

            var tessera = new Border
            {
                WidthRequest = 190, HeightRequest = 140, Margin = new Thickness(6),
                StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                BackgroundColor = Ui.ColoreSicuro(c.Colore, "#2196F3"),
                Opacity = c.Attiva ? 1.0 : 0.55,
                Content = griglia
            };
            Ui.Tocco(centro, async () => await Navigation.PushAsync(new CategoriaPage(c)));
            return tessera;
        }

        private async Task Sposta(Categoria c, int direzione) => await Esegui(() => ContestoCassa.GestioneCatalogo.SpostaCategoria(c.Id, direzione));

        private async void OnNuovaCategoria(object sender, EventArgs e) => await Navigation.PushAsync(new CategoriaPage(null));
    }
}
