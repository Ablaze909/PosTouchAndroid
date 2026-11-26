using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Dialoghi
{
    public class EsitoPagamento
    {
        public string Metodo { get; set; } = MetodiPagamento.Contanti;
        public int ContantiCent { get; set; }
        public int CartaCent { get; set; }
        public int RicevutoCent { get; set; }
        public int RestoCent { get; set; }
    }

    /// <summary>
    /// Copia di CalcoloRestoDialog del PC (tocco sulla barra TOTALE):
    /// banconote e monete a sinistra, metodo / mancano / tastiera al centro, 🧾 Scontrino / RESTO / Annulla a destra.
    /// Stessa logica: contanti, carta, oppure misto (carta + contanti).
    /// </summary>
    public partial class CalcoloResto : DialogoBase<EsitoPagamento>
    {
        private readonly decimal _totale;
        private decimal _contantiRicevuti;
        private decimal _cartaImporto;
        private string _input = "";
        private bool _modoCarta;
        private const decimal MaxContanti = 99999.99m;

        private readonly Label _mancano, _importo, _riepilogo, _resto;
        private readonly Border _boxMancano, _boxRiepilogo, _boxResto;
        private readonly Border _voceContanti, _voceCarta, _btnScontrino;

        private decimal ParteContanti => Math.Max(0, _totale - _cartaImporto);
        private decimal Resto => Math.Max(0, _contantiRicevuti - ParteContanti);
        private bool PagamentoCompleto => _totale > 0 && (
            _cartaImporto >= _totale ||
            (_cartaImporto > 0 && _cartaImporto < _totale) ||
            _contantiRicevuti >= _totale);

        public CalcoloResto(decimal totale)
        {
            _totale = totale;

            // ═══ SINISTRA: banconote + monete ═══
            var sinistra = new Grid { ColumnDefinitions = { new(GridLength.Star), new(new GridLength(80)) }, Margin = new Thickness(0, 0, 8, 0) };
            for (int i = 0; i < 7; i++) sinistra.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            var banconote = new (decimal v, string a, string b, string bordo, string testo)[]
            {
                (500, "#7B1FA2", "#AB47BC", "#6A1B9A", "#FFFFFF"),
                (200, "#F9A825", "#FFEE58", "#F57F17", "#5D4037"),
                (100, "#2E7D32", "#66BB6A", "#1B5E20", "#FFFFFF"),
                (50,  "#E65100", "#FFB74D", "#BF360C", "#FFFFFF"),
                (20,  "#1565C0", "#64B5F6", "#0D47A1", "#FFFFFF"),
                (10,  "#C62828", "#E57373", "#B71C1C", "#FFFFFF"),
                (5,   "#455A64", "#90A4AE", "#37474F", "#FFFFFF")
            };
            for (int i = 0; i < banconote.Length; i++)
            {
                var b = banconote[i];
                var nota = new Border
                {
                    Margin = new Thickness(3),
                    StrokeShape = new RoundRectangle { CornerRadius = 5 },
                    Stroke = new SolidColorBrush(Ui.C(b.bordo)), StrokeThickness = 1.5,
                    Background = new LinearGradientBrush(new GradientStopCollection
                    {
                        new GradientStop(Ui.C(b.a), 0f), new GradientStop(Ui.C(b.b), 0.5f), new GradientStop(Ui.C(b.a), 1f)
                    }, new Point(0, 0), new Point(1, 0.3)),
                    Content = new Label { Text = $"€{b.v:0}", FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Ui.C(b.testo),
                                          HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
                };
                decimal valore = b.v;
                Ui.Tocco(nota, () => Taglio(valore));
                sinistra.Add(nota, 0, i);
            }
            var monete = new (decimal v, string testo, double d, string fill, string ring)[]
            {
                (2m, "€2", 62, "#E0E0E0", "#C49B00"),
                (1m, "€1", 58, "#FFD54F", "#9E9E9E"),
                (0.50m, "50c", 56, "#FFD54F", "#C49B00"),
                (0.20m, "20c", 50, "#FFD54F", "#C49B00"),
                (0.10m, "10c", 46, "#FFD54F", "#C49B00"),
                (0.05m, "5c", 40, "#BCAAA4", "#6D4C41"),
                (0.02m, "2c", 34, "#BCAAA4", "#6D4C41")
            };
            for (int i = 0; i < monete.Length; i++)
            {
                var m = monete[i];
                var moneta = new Border
                {
                    WidthRequest = m.d, HeightRequest = m.d,
                    HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                    StrokeShape = new Ellipse(),
                    Stroke = new SolidColorBrush(Ui.C(m.ring)), StrokeThickness = 4,
                    BackgroundColor = Ui.C(m.fill),
                    Content = new Label { Text = m.testo, FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#4E342E"),
                                          HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
                };
                decimal valore = m.v;
                Ui.Tocco(moneta, () => Taglio(valore));
                sinistra.Add(moneta, 1, i);
            }

            // ═══ CENTRO: metodo, mancano, importo, tastiera, riepilogo ═══
            _voceContanti = Voce("Contanti", () => ScegliMetodo(false));
            _voceCarta = Voce("Carta di Credito", () => ScegliMetodo(true));
            var metodi = Ui.Riquadro(new VerticalStackLayout { Children = { _voceContanti, _voceCarta } }, "#FFFFFF", 0, null, "#B0BEC5", 1);
            metodi.Margin = new Thickness(0, 0, 0, 6);

            _mancano = new Label { FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center };
            _boxMancano = Ui.Riquadro(_mancano, "#E91E63", 6, new Thickness(12, 10));
            _boxMancano.Margin = new Thickness(0, 0, 0, 6);

            _importo = new Label { Text = "0,00", FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#263238"), HorizontalTextAlignment = TextAlignment.End };
            var boxImporto = Ui.Riquadro(_importo, "#FFFFFF", 6, new Thickness(12, 8), "#B0BEC5", 1);
            boxImporto.Margin = new Thickness(0, 0, 0, 6);

            var tastiera = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(new GridLength(80)) } };
            for (int i = 0; i < 4; i++) tastiera.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            string[,] tasti = { { "7", "8", "9", "C" }, { "4", "5", "6", "✕" }, { "1", "2", "3", "✓" }, { ",", "0", "00", "⌫" } };
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                {
                    string t = tasti[r, c];
                    Button b = c < 3
                        ? Ui.Tasto(t, "#FFFFFF", "#111111", 26, 8, (s, e) => Cifra(t))
                        : t switch
                        {
                            "C" => Ui.Tasto("C", "#F44336", fontSize: 24, clic: (s, e) => Cancella()),
                            "✕" => Ui.Tasto("✕", "#E91E63", fontSize: 24, clic: (s, e) => Chiudi(null)),
                            "✓" => Ui.Tasto("✓", "#4CAF50", fontSize: 24, clic: (s, e) => ConfermaImporto()),
                            _ => Ui.Tasto("⌫", "#FF9800", fontSize: 24, clic: (s, e) => Backspace())
                        };
                    if (c < 3) { b.BorderColor = Ui.C("#D0D0D0"); b.BorderWidth = 1; }
                    b.Margin = new Thickness(3);
                    tastiera.Add(b, c, r);
                }

            _riepilogo = new Label { FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#1565C0"), HorizontalOptions = LayoutOptions.Center };
            _boxRiepilogo = Ui.Riquadro(_riepilogo, "#E3F2FD", 6, new Thickness(10, 8));
            _boxRiepilogo.Margin = new Thickness(0, 6, 0, 0);
            _boxRiepilogo.IsVisible = false;

            var centro = new Grid
            {
                RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
                Margin = new Thickness(4, 0, 8, 0)
            };
            centro.Add(metodi, 0, 0);
            centro.Add(_boxMancano, 0, 1);
            centro.Add(boxImporto, 0, 2);
            centro.Add(tastiera, 0, 3);
            centro.Add(_boxRiepilogo, 0, 4);

            // ═══ DESTRA: 🧾 Scontrino, RESTO, Annulla ═══
            _btnScontrino = TastoGrande("🧾", "Scontrino", "#66BB6A", "#388E3C", 130, Scontrino);
            _btnScontrino.Margin = new Thickness(0, 0, 0, 8);
            _resto = new Label { FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#2E7D32"), HorizontalOptions = LayoutOptions.Center };
            _boxResto = Ui.Riquadro(new VerticalStackLayout
            {
                HorizontalOptions = LayoutOptions.Center,
                Children = { new Label { Text = "RESTO", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#2E7D32"), HorizontalOptions = LayoutOptions.Center }, _resto }
            }, "#E8F5E9", 10, new Thickness(8, 12));
            _boxResto.Margin = new Thickness(0, 0, 0, 8);
            _boxResto.IsVisible = false;
            var annulla = TastoGrande(null, "Annulla", "#BDBDBD", "#757575", 70, () => Chiudi(null));

            var destra = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
            destra.Add(_btnScontrino, 0, 0);
            destra.Add(_boxResto, 0, 1);
            destra.Add(annulla, 0, 3);

            var corpo = new Grid { ColumnDefinitions = { new(new GridLength(220)), new(GridLength.Star), new(new GridLength(140)) }, Padding = new Thickness(10) };
            corpo.Add(sinistra, 0, 0);
            corpo.Add(centro, 1, 0);
            corpo.Add(destra, 2, 0);

            var riquadro = new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Ui.C("#ECEFF1"),
                Stroke = new SolidColorBrush(Ui.C("#90A4AE")), StrokeThickness = 1.5,
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                Content = corpo
            };
            Content = riquadro;
            Dimensiona(riquadro, 820, 700);
            Aggiorna();
        }

        // ══════════════ costruzione ══════════════

        private static Border Voce(string testo, Action azione)
        {
            var v = new Border
            {
                StrokeThickness = 0, Padding = new Thickness(14, 10),
                Content = new Label { Text = testo, FontSize = 18, FontAttributes = FontAttributes.Bold }
            };
            Ui.Tocco(v, azione);
            return v;
        }

        private static Border TastoGrande(string icona, string testo, string alto, string basso, double altezza, Action azione)
        {
            var stack = new VerticalStackLayout { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            if (icona != null) stack.Add(new Label { Text = icona, FontSize = 36, HorizontalOptions = LayoutOptions.Center });
            stack.Add(new Label { Text = testo, FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center });
            var b = new Border
            {
                HeightRequest = altezza,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Ui.C(alto), 0f), new GradientStop(Ui.C(basso), 1f)
                }, new Point(0, 0), new Point(0, 1)),
                Content = stack
            };
            Ui.Tocco(b, azione);
            return b;
        }

    }
}
