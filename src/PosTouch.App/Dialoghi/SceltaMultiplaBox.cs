using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;

namespace PosTouch.App.Dialoghi
{
    /// <summary>Scelta di più voci (tasti che si accendono/spengono) — es. le categorie di una variante. null = annullato.</summary>
    public class SceltaMultiplaBox : DialogoBase<List<int>>
    {
        private readonly HashSet<int> _scelte;

        private SceltaMultiplaBox(string titolo, IList<string> voci, IEnumerable<int> selezionate)
        {
            _scelte = new HashSet<int>(selezionate);
            var flex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            for (int i = 0; i < voci.Count; i++)
            {
                int k = i;
                var b = Ui.Tasto(voci[i], "#FFFFFF", "#37474F", 15, 8, altezza: 52);
                b.Margin = new Thickness(4);
                b.Padding = new Thickness(14, 0);
                void Colora() { bool on = _scelte.Contains(k); b.BackgroundColor = Ui.C(on ? "#43A047" : "#FFFFFF"); b.TextColor = Ui.C(on ? "#FFFFFF" : "#37474F"); }
                b.Clicked += (s, e) => { if (!_scelte.Remove(k)) _scelte.Add(k); Colora(); };
                Colora();
                flex.Add(b);
            }
            var azioni = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
            azioni.Add(Ui.Tasto("ANNULLA", "#9E9E9E", altezza: 52, clic: (s, e) => Chiudi(null)), 0, 0);
            azioni.Add(Ui.Tasto("OK", "#4CAF50", altezza: 52, clic: (s, e) => Chiudi(_scelte.OrderBy(x => x).ToList())), 1, 0);
            var corpo = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(18), RowSpacing = 12 };
            corpo.Add(new Label { Text = titolo, FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#37474F"), HorizontalOptions = LayoutOptions.Center }, 0, 0);
            corpo.Add(new ScrollView { Content = flex }, 0, 1);
            corpo.Add(azioni, 0, 2);
            var riquadro = new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Ui.C("#ECEFF1"), StrokeShape = new RoundRectangle { CornerRadius = 12 }, StrokeThickness = 0, Content = corpo
            };
            Content = riquadro;
            Dimensiona(riquadro, 720, 560);
        }

        /// <summary>Restituisce gli indici scelti (null = annullato).</summary>
        public static Task<List<int>> Chiedi(string titolo, IList<string> voci, IEnumerable<int> selezionate)
            => Dialogo.Mostra(new SceltaMultiplaBox(titolo, voci, selezionate));
    }
}
