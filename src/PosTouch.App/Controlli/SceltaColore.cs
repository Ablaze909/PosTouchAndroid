namespace PosTouch.App.Controlli
{
    /// <summary>Tavolozza di colori per i tasti (categorie e articoli). Valore: "#RRGGBB".</summary>
    public class SceltaColore : ContentView
    {
        public static readonly string[] Tavolozza =
        {
            "#C62828", "#AD1457", "#6A1B9A", "#4527A0", "#1565C0", "#0277BD", "#00838F", "#00695C",
            "#2E7D32", "#558B2F", "#9E9D24", "#F9A825", "#EF6C00", "#D84315", "#4E342E", "#455A64"
        };

        private readonly List<Border> _caselle = new List<Border>();
        private string _colore = Tavolozza[4];

        public event Action Cambiato;

        public string Colore
        {
            get => _colore;
            set { _colore = string.IsNullOrWhiteSpace(value) ? Tavolozza[4] : value.ToUpperInvariant(); Evidenzia(); }
        }

        public SceltaColore()
        {
            var flex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (var hex in Tavolozza)
            {
                var b = new Border
                {
                    BackgroundColor = Color.FromArgb(hex),
                    WidthRequest = 44,
                    HeightRequest = 44,
                    Margin = new Thickness(0, 0, 6, 6),
                    StrokeThickness = 0,
                    Stroke = new SolidColorBrush(Colors.Black),
                    ClassId = hex,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 }
                };
                var tap = new TapGestureRecognizer();
                tap.Tapped += (s, e) => { Colore = hex; Cambiato?.Invoke(); };
                b.GestureRecognizers.Add(tap);
                _caselle.Add(b);
                flex.Children.Add(b);
            }
            Content = flex;
            Evidenzia();
        }

        private void Evidenzia()
        {
            foreach (var b in _caselle)
                b.StrokeThickness = string.Equals(b.ClassId, _colore, StringComparison.OrdinalIgnoreCase) ? 4 : 0;
        }
    }
}
