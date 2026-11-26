using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;

namespace PosTouch.App.Dialoghi
{
    /// <summary>
    /// Copia di CustomMessageBox del PC: riquadro scuro #1e293b, icona grande, titolo bianco,
    /// messaggio grigio chiaro, uno o due pulsanti colorati.
    /// </summary>
    public class MessaggioBox : DialogoBase<bool>
    {
        private MessaggioBox(string titolo, string messaggio, string icona, string tastoSinistro, string tastoDestro,
                             string coloreSinistro, string coloreDestro)
        {
            var g = new Grid
            {
                RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) },
                Padding = new Thickness(32, 28, 32, 28)
            };
            g.Add(new Label { Text = icona, FontSize = 52, HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 0, 16) }, 0, 0);
            g.Add(new Label { Text = titolo, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Colors.White,
                              HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 12) }, 0, 1);
            g.Add(new ScrollView
            {
                MaximumHeightRequest = 380,
                Content = new Label { Text = messaggio, FontSize = 15, TextColor = Ui.C("#cbd5e1"), HorizontalTextAlignment = TextAlignment.Center,
                                      LineHeight = 1.3 }
            }, 0, 2);

            var tasti = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 24, 0, 0) };
            if (tastoSinistro != null)
            {
                tasti.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                tasti.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                tasti.Add(Ui.Tasto(tastoSinistro, coloreSinistro, altezza: 52, clic: (s, e) => Chiudi(false)), 0, 0);
                tasti.Add(Ui.Tasto(tastoDestro, coloreDestro, altezza: 52, clic: (s, e) => Chiudi(true)), 1, 0);
            }
            else
            {
                tasti.Add(Ui.Tasto(tastoDestro, coloreDestro, altezza: 52, clic: (s, e) => Chiudi(true)), 0, 0);
            }
            g.Add(tasti, 0, 3);

            Content = new Border
            {
                WidthRequest = 480,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Ui.C("#1e293b"),
                Stroke = new SolidColorBrush(Ui.C("#334155")),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                Content = g
            };
        }

        public static Task Info(string titolo, string messaggio) => Dialogo.Mostra(new MessaggioBox(titolo, messaggio, "ℹ️", null, "OK", null, "#2196F3"));
        public static Task Successo(string titolo, string messaggio) => Dialogo.Mostra(new MessaggioBox(titolo, messaggio, "✅", null, "OK", null, "#4CAF50"));
        public static Task Errore(string titolo, string messaggio) => Dialogo.Mostra(new MessaggioBox(titolo, messaggio, "❌", null, "OK", null, "#F44336"));
        public static Task Avviso(string titolo, string messaggio) => Dialogo.Mostra(new MessaggioBox(titolo, messaggio, "⚠️", null, "OK", null, "#FF9800"));
        /// <summary>Domanda NO / SÌ.</summary>
        public static Task<bool> Domanda(string titolo, string messaggio) => Dialogo.Mostra(new MessaggioBox(titolo, messaggio, "❓", "NO", "SÌ", "#9E9E9E", "#4CAF50"));
        /// <summary>Conferma critica ANNULLA / CONFERMA (rosso).</summary>
        public static Task<bool> Conferma(string titolo, string messaggio) => Dialogo.Mostra(new MessaggioBox(titolo, messaggio, "⚠️", "ANNULLA", "CONFERMA", "#9E9E9E", "#F44336"));
    }
}
