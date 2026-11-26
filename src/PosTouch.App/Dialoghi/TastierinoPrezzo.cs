using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;

namespace PosTouch.App.Dialoghi
{
    public enum ModoTastierino { Euro, Quantita, Codice }

    /// <summary>
    /// Copia di TastierinoPrezzoDialog del PC (500×600, bordo nero, display verde, tastiera 4×3, ANNULLA/OK).
    /// Euro: "€ 0,00"; Quantita: numero; Codice: cifre (reparto, barcode, password).
    /// Restituisce il testo digitato (null = annullato).
    /// </summary>
    public class TastierinoPrezzo : DialogoBase<string>
    {
        private readonly ModoTastierino _modo;
        private readonly Label _display;
        private readonly bool _nascosto;
        private string _input = "";

        public TastierinoPrezzo(string titolo, string valoreAttuale, ModoTastierino modo, bool nascondi = false)
        {
            _modo = modo;
            _nascosto = nascondi;
            _display = new Label { FontSize = 36, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.End };
            var attuale = new Label
            {
                Text = string.IsNullOrEmpty(valoreAttuale) ? "" : $"(Attuale: {valoreAttuale})",
                FontSize = 12, TextColor = Colors.White, Opacity = 0.8, HorizontalTextAlignment = TextAlignment.End, Margin = new Thickness(0, 5, 0, 0),
                IsVisible = !string.IsNullOrEmpty(valoreAttuale)
            };
            var display = Ui.Riquadro(new VerticalStackLayout { Children = { _display, attuale } }, "#4CAF50", 8, new Thickness(15));
            display.Margin = new Thickness(0, 0, 0, 5);

            var tastiera = Ui.Griglia(4, 3);
            string[] tasti = { "7", "8", "9", "4", "5", "6", "1", "2", "3", modo == ModoTastierino.Codice ? "00" : ",", "0", "C" };
            for (int i = 0; i < tasti.Length; i++)
            {
                string t = tasti[i];
                Button b = t == "C"
                    ? Ui.Tasto("C", "#F44336", "#FFFFFF", 24, 8, (s, e) => { _input = ""; Aggiorna(); })
                    : Ui.Tasto(t, "#FFFFFF", "#37474F", 24, 8, (s, e) => Premi(t));
                b.BorderColor = Ui.C("#E0E0E0");
                b.BorderWidth = t == "C" ? 0 : 1;
                b.Margin = new Thickness(4);
                tastiera.Add(b, i % 3, i / 3);
            }

            var azioni = new Grid { ColumnDefinitions = { new(GridLength.Star), new(new GridLength(10)), new(GridLength.Star) }, Margin = new Thickness(0, 15, 0, 0) };
            azioni.Add(Ui.Tasto("ANNULLA", "#9E9E9E", altezza: 55, clic: (s, e) => Chiudi(null)), 0, 0);
            azioni.Add(Ui.Tasto("OK", "#4CAF50", altezza: 55, clic: (s, e) => Conferma()), 2, 0);

            var corpo = new Grid
            {
                RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
                Padding = new Thickness(25)
            };
            corpo.Add(new Label { Text = titolo, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#37474F"),
                                  HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 15) }, 0, 0);
            corpo.Add(display, 0, 1);
            corpo.Add(tastiera, 0, 2);
            corpo.Add(azioni, 0, 3);

            var riquadro = new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Colors.White, Stroke = new SolidColorBrush(Colors.Black), StrokeThickness = 3,
                StrokeShape = new Rectangle(),
                Content = corpo
            };
            Content = riquadro;
            Dimensiona(riquadro, 500, 600);
            Aggiorna();
        }

        private void Premi(string t)
        {
            if (_modo == ModoTastierino.Codice)
            {
                if (_input.Length + t.Length <= 20) _input += t;   // barcode, reparto, password
                Aggiorna();
                return;
            }
            // Importi e quantità: max 5 cifre intere (99.999), 2 decimali per gli euro, 3 per i pesi
            int virgola = _input.IndexOf(',');
            int maxDecimali = _modo == ModoTastierino.Euro ? 2 : 3;
            if (t == ",")
            {
                if (virgola < 0) _input += _input.Length == 0 ? "0," : ",";
            }
            else if (virgola < 0 ? _input.Length < 5 : _input.Length - virgola - 1 < maxDecimali)
            {
                _input = _input == "0" ? t : _input + t;
            }
            Aggiorna();
        }

        private void Aggiorna()
        {
            if (_modo == ModoTastierino.Euro)
                _display.Text = $"€ {Valore():N2}";
            else if (_nascosto)
                _display.Text = new string('●', _input.Length);
            else
                _display.Text = _input.Length == 0 ? "0" : _input;
        }

        private decimal Valore() =>
            decimal.TryParse(_input.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : 0;

        private void Conferma()
        {
            if (_modo == ModoTastierino.Codice) Chiudi(_input);
            else Chiudi(Valore().ToString(CultureInfo.InvariantCulture));
        }

        // ── scorciatoie ──

        /// <summary>Prezzo in euro (null = annullato).</summary>
        public static async Task<decimal?> ChiediEuro(string titolo, decimal? attuale = null)
        {
            var r = await Dialogo.Mostra(new TastierinoPrezzo(titolo, attuale.HasValue ? $"€ {attuale.Value:N2}" : null, ModoTastierino.Euro));
            return r == null ? null : decimal.Parse(r, CultureInfo.InvariantCulture);
        }

        public static async Task<decimal?> ChiediQuantita(string titolo, decimal? attuale = null)
        {
            var r = await Dialogo.Mostra(new TastierinoPrezzo(titolo, attuale?.ToString("0.###"), ModoTastierino.Quantita));
            return r == null ? null : decimal.Parse(r, CultureInfo.InvariantCulture);
        }

        public static Task<string> ChiediCodice(string titolo, string attuale = null, bool nascondi = false)
            => Dialogo.Mostra(new TastierinoPrezzo(titolo, attuale, ModoTastierino.Codice, nascondi));
    }
}
