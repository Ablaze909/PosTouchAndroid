using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;

namespace PosTouch.App.Dialoghi
{
    /// <summary>
    /// Sconto sul subtotale: percentuale o importo si sceglie DENTRO la finestra (prima: una domanda in più),
    /// con 5-10-15-20% rapidi. Mostra subito quanto si toglie. Restituisce (percentuale?, valore) o null.
    /// </summary>
    public class ScontoBox : DialogoBase<(bool percentuale, decimal valore)?>
    {
        private bool _percentuale = true;
        private readonly decimal _subtotale;
        private readonly Label _display = new Label();
        private readonly Label _effetto = new Label { Text = " " };
        private readonly Label _errore;
        private readonly Button _btnPerc, _btnEuro;
        private readonly Grid _veloci;
        private readonly List<Button> _tasti = new List<Button>();
        private string _input = "";

        public ScontoBox(decimal subtotale)
        {
            _subtotale = subtotale;
            var t = StileFinestre.T;
            _errore = new Label { FontSize = 13, TextColor = Ui.C("#E53935"), HorizontalTextAlignment = TextAlignment.Center, IsVisible = false };

            // scelta % / € (interruttore a due posizioni)
            _btnPerc = Scelta("%  PERCENTUALE", () => Modo(true));
            _btnEuro = Scelta("€  IMPORTO", () => Modo(false));
            var interruttore = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 4 };
            interruttore.Add(_btnPerc, 0, 0);
            interruttore.Add(_btnEuro, 1, 0);
            var cornice = new Border { BackgroundColor = t.Campo, Stroke = new SolidColorBrush(t.Bordo), StrokeThickness = 1,
                                       StrokeShape = new RoundRectangle { CornerRadius = 12 }, Padding = new Thickness(3), Content = interruttore };

            // percentuali rapide
            _veloci = new Grid { ColumnSpacing = 6 };
            int col = 0;
            foreach (var v in new[] { 5, 10, 15, 20 })
            {
                _veloci.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                var b = StileFinestre.TastoSecondario($"{v}%", (s, e) => { _input = v.ToString(); Aggiorna(); }, 38);
                b.CornerRadius = 9;
                _veloci.Add(b, col++, 0);
            }

            var virgola = StileFinestre.TastoCifra(",", (s, e) => Premi(","));
            var cancella = StileFinestre.TastoIcona(StileFinestre.IcoCancella, "Cancella", (s, e) => { if (_input.Length > 0) _input = _input[..^1]; Aggiorna(); });
            var tastiera = StileFinestre.Tastierino(Premi, virgola, cancella, _tasti);

            var corpo = new VerticalStackLayout { Spacing = 10 };
            corpo.Add(StileFinestre.Intestazione(StileFinestre.IcoSconto, "SCONTO", $"subtotale € {subtotale:N2}", () => Chiudi(null)));
            corpo.Add(cornice);
            corpo.Add(StileFinestre.Campo(_display, _effetto));
            corpo.Add(_veloci);
            corpo.Add(tastiera);
            corpo.Add(_errore);
            corpo.Add(StileFinestre.Azioni(StileFinestre.TastoSecondario("ANNULLA", (s, e) => Chiudi(null)),
                                           StileFinestre.TastoPrincipale("APPLICA", (s, e) => Applica())));
            var riquadro = StileFinestre.Scheda(corpo);   // senza ScrollView: niente spazio vuoto sotto
            Content = riquadro;
            Dimensiona(riquadro, 350);
            Modo(true);
        }

        private Button Scelta(string testo, Action azione)
        {
            var b = new Button { Text = testo, FontSize = 14, FontAttributes = FontAttributes.Bold, CornerRadius = 9, HeightRequest = 40, Padding = 0 };
            b.Clicked += (s, e) => azione();
            return b;
        }

        private void Modo(bool percentuale)
        {
            if (_percentuale != percentuale) _input = "";
            _percentuale = percentuale;
            var t = StileFinestre.T;
            _btnPerc.BackgroundColor = percentuale ? StileFinestre.Verde : Colors.Transparent;
            _btnPerc.TextColor = percentuale ? Colors.White : t.Muto;
            _btnEuro.BackgroundColor = !percentuale ? StileFinestre.Verde : Colors.Transparent;
            _btnEuro.TextColor = !percentuale ? Colors.White : t.Muto;
            _veloci.IsVisible = percentuale;
            Aggiorna();
        }

        /// <summary>Schermo basso (8"): i tasti si accorciano perché ANNULLA/APPLICA restino in vista.</summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (height <= 0) return;
            double tasto = Math.Clamp((height - 24 - 370) / 4, 40, 52);
            foreach (var b in _tasti) if (Math.Abs(b.HeightRequest - tasto) > 0.5) b.HeightRequest = tasto;
        }

        private void Premi(string c)
        {
            if (c == ",") { if (!_input.Contains(",")) _input += _input.Length == 0 ? "0," : ","; }
            else if (_input.Length < 8) _input = _input == "0" ? c : _input + c;
            Aggiorna();
        }

        private decimal Valore()
        {
            decimal.TryParse(_input.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v);
            return v;
        }

        private void Aggiorna()
        {
            _errore.IsVisible = false;
            string n = _input.Length == 0 ? "0" : _input;
            _display.Text = _percentuale ? $"{n} %" : $"€ {n}";
            decimal v = Valore();
            decimal togli = _percentuale ? Math.Round(_subtotale * v / 100m, 2, MidpointRounding.AwayFromZero) : v;
            _effetto.Text = v > 0 ? $"= − € {togli:N2}" : " ";
        }

        private void Applica()
        {
            decimal v = Valore();
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
            Chiudi((_percentuale, v));
        }

        public static Task<(bool percentuale, decimal valore)?> Chiedi(decimal subtotale) => Dialogo.Mostra(new ScontoBox(subtotale));
    }
}
