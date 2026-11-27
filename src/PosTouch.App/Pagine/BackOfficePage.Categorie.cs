using System.ComponentModel;
using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    public partial class BackOfficePage
    {
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
                ColumnDefinitions = { new(GridLength.Star), new(new GridLength(44)), new(new GridLength(44)) },
                Padding = new Thickness(8)
            };
            if (!c.Attiva)
                griglia.Add(Ui.Riquadro(new Label { Text = "NASCOSTA", FontSize = 9, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                                        "#80FF0000", 4, new Thickness(4, 2)), 0, 0);
            var su = Ui.Riquadro(new Label { Text = "▲", FontSize = 16, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
                                 "#FFFFFF", 5, new Thickness(0, 4));
            var giu = Ui.Riquadro(new Label { Text = "▼", FontSize = 16, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
                                  "#FFFFFF", 5, new Thickness(0, 4));
            su.Margin = giu.Margin = new Thickness(2, 0);
            su.HeightRequest = giu.HeightRequest = 40;   // frecce toccabili col dito (prima circa 32×24)
            Ui.Tocco(su, async () => { _toccoFreccia = DateTime.Now; await Sposta(c, -1); });
            Ui.Tocco(giu, async () => { _toccoFreccia = DateTime.Now; await Sposta(c, +1); });
            griglia.Add(su, 1, 0);
            griglia.Add(giu, 2, 0);

            var centro = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
            if (!string.IsNullOrWhiteSpace(c.Icona)) centro.Add(new Label { Text = c.Icona, FontSize = 22, HorizontalOptions = LayoutOptions.Center });
            centro.Add(new Label { Text = c.Nome, FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = testo, HorizontalTextAlignment = TextAlignment.Center, ClassId = StilePagine.ColoreUtente });
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
                ClassId = StilePagine.ColoreUtente,   // colore scelto dall'utente: il tema non lo cambia
                Opacity = c.Attiva ? 1.0 : 0.55,
                Content = griglia
            };
            // Tocco su tutta la tessera (non solo sul nome): apre subito la modifica
            Ui.Tocco(tessera, async () =>
            {
                await Task.Delay(60); // lascia passare prima l'eventuale tocco su ▲▼
                if ((DateTime.Now - _toccoFreccia).TotalMilliseconds < 400) return; // era una freccia ▲▼
                await Navigazione.Apri(this, () => new CategoriaPage(c));
            });
            return tessera;
        }

        private async Task Sposta(Categoria c, int direzione) => await Esegui(() => ContestoCassa.GestioneCatalogo.SpostaCategoria(c.Id, direzione));

        private async void OnNuovaCategoria(object sender, EventArgs e) => await Navigazione.Apri(this, () => new CategoriaPage(null));
    }
}
