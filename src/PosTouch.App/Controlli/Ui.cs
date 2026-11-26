using Microsoft.Maui.Controls.Shapes;

namespace PosTouch.App.Controlli
{
    /// <summary>Piccoli costruttori per tasti e testi nello stile POS Touch (usati da popup e pagine create da codice).</summary>
    public static class Ui
    {
        public static Color C(string hex) => Color.FromArgb(hex);

        public static Color ColoreSicuro(string hex, string predefinito)
            => !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out var c) ? c : Color.FromArgb(predefinito);

        public static Button Tasto(string testo, string sfondo, string colTesto = "#FFFFFF", double fontSize = 16,
                                   int raggio = 8, EventHandler clic = null, double altezza = -1)
        {
            var b = new Button
            {
                Text = testo,
                BackgroundColor = C(sfondo),
                TextColor = C(colTesto),
                FontSize = fontSize,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = raggio,
                Padding = new Thickness(4, 0),
                BorderWidth = 0
            };
            if (altezza > 0) b.HeightRequest = altezza;
            if (clic != null) b.Clicked += clic;
            return b;
        }

        public static Label Testo(string testo, double fontSize = 14, string colore = "#111111", bool grassetto = false,
                                  TextAlignment allinea = TextAlignment.Start)
            => new Label
            {
                Text = testo,
                FontSize = fontSize,
                TextColor = C(colore),
                FontAttributes = grassetto ? FontAttributes.Bold : FontAttributes.None,
                HorizontalTextAlignment = allinea,
                LineBreakMode = LineBreakMode.WordWrap
            };

        public static Border Riquadro(View contenuto, string sfondo, double raggio = 0, Thickness? padding = null,
                                      string bordo = null, double spessore = 0)
            => new Border
            {
                Content = contenuto,
                BackgroundColor = C(sfondo),
                StrokeShape = new RoundRectangle { CornerRadius = raggio },
                Stroke = bordo == null ? null : new SolidColorBrush(C(bordo)),
                StrokeThickness = bordo == null ? 0 : spessore,
                Padding = padding ?? new Thickness(0)
            };

        public static void Tocco(View vista, Action azione)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => azione();
            vista.GestureRecognizers.Add(tap);
        }

        public static Grid Griglia(int righe, int colonne, double spazio = 0)
        {
            var g = new Grid { RowSpacing = spazio, ColumnSpacing = spazio };
            for (int i = 0; i < righe; i++) g.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            for (int i = 0; i < colonne; i++) g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            return g;
        }
    }
}
