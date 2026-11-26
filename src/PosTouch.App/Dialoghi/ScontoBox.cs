using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;

namespace PosTouch.App.Dialoghi
{
    /// <summary>Copia di ScontoDialog del PC: percentuale o valore, display grigio, tastiera con ⌫, ANNULLA/APPLICA.</summary>
    public class ScontoBox : DialogoBase<decimal?>
    {
        private readonly bool _percentuale;
        private readonly decimal _subtotale;
        private readonly Label _display;
        private readonly Label _errore;
        private string _input = "";

        public ScontoBox(bool percentuale, decimal subtotale)
        {
            _percentuale = percentuale;
            _subtotale = subtotale;
            _display = new Label { Text = "0", FontSize = 40, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#37474F"), HorizontalTextAlignment = TextAlignment.End };
            _errore = new Label { FontSize = 13, TextColor = Ui.C("#F44336"), HorizontalTextAlignment = TextAlignment.Center, IsVisible = false };

            var tastiera = Ui.Griglia(4, 3);
            string[] tasti = { "7", "8", "9", "4", "5", "6", "1", "2", "3", ",", "0", "⌫" };
            for (int i = 0; i < tasti.Length; i++)
            {
                string t = tasti[i];
                Button b = t == "⌫"
                    ? Ui.Tasto("⌫", "#FF9800", "#FFFFFF", 24, 8, (s, e) => { if (_input.Length > 0) _input = _input[..^1]; Aggiorna(); })
                    : Ui.Tasto(t, "#FFFFFF", "#37474F", 24, 8, (s, e) => Premi(t));
                b.BorderColor = Ui.C("#E0E0E0");
                b.BorderWidth = t == "⌫" ? 0 : 1;
                b.Margin = new Thickness(4);
                b.HeightRequest = 64;
                tastiera.Add(b, i % 3, i / 3);
            }

            var azioni = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10, Margin = new Thickness(0, 20, 0, 0) };
            azioni.Add(Ui.Tasto("ANNULLA", "#9E9E9E", fontSize: 18, altezza: 55, clic: (s, e) => Chiudi(null)), 0, 0);
            azioni.Add(Ui.Tasto("APPLICA", "#4CAF50", fontSize: 18, altezza: 55, clic: (s, e) => Applica()), 1, 0);

            var corpo = new VerticalStackLayout { Padding = new Thickness(25) };
            corpo.Add(new Label
            {
                Text = percentuale ? "SCONTO PERCENTUALE" : $"SCONTO A VALORE\n(max € {subtotale:N2})",
                FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#37474F"),
                HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 15)
            });
            var disp = Ui.Riquadro(_display, "#ECEFF1", 8, new Thickness(15));
            disp.Margin = new Thickness(0, 0, 0, 20);
            corpo.Add(disp);
            corpo.Add(tastiera);
            corpo.Add(_errore);
            corpo.Add(azioni);

            Content = new Border
            {
                WidthRequest = 400,
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Colors.White, StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                Content = corpo
            };
        }

        private void Premi(string t)
        {
            if (t == ",") { if (!_input.Contains(",")) _input += _input.Length == 0 ? "0," : ","; }
            else if (_input.Length < 8) _input += t;
            Aggiorna();
        }

        private void Aggiorna()
        {
            _errore.IsVisible = false;
            _display.Text = (_input.Length == 0 ? "0" : _input) + (_percentuale ? " %" : " €");
        }

        private void Applica()
        {
            decimal.TryParse(_input.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v);
            string err = null;
            if (v <= 0) err = "Inserire un valore maggiore di zero";
            else if (_percentuale && v >= 100) err = "La percentuale deve essere minore di 100";
            else if (!_percentuale && v >= _subtotale) err = $"Lo sconto deve essere minore di € {_subtotale:N2}";
            if (err != null)
            {
                _errore.Text = err;
                _errore.IsVisible = true;
                return;
            }
            Chiudi(v);
        }

        public static Task<decimal?> Chiedi(bool percentuale, decimal subtotale) => Dialogo.Mostra(new ScontoBox(percentuale, subtotale));
    }
}
