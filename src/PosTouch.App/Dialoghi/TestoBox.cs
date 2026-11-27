using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;

namespace PosTouch.App.Dialoghi
{
    /// <summary>Richiesta di un testo (es. 👤 NOME CLIENTE) con la tastiera di Android. null = annullato.</summary>
    public class TestoBox : DialogoBase<string>
    {
        private readonly Entry _campo;

        public TestoBox(string titolo, string valore, int lunghezzaMax, bool codice = false)
        {
            _campo = new Entry { Text = valore ?? "", FontSize = 22, MaxLength = lunghezzaMax, BackgroundColor = Ui.C("#FAFAFA"), Keyboard = codice ? Keyboard.Plain : Keyboard.Text };
            if (codice) { _campo.IsTextPredictionEnabled = false; _campo.IsSpellCheckEnabled = false; }   // barcode: niente suggerimenti
            _campo.Completed += (s, e) => Conferma();
            var azioni = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
            azioni.Add(Ui.Tasto("ANNULLA", "#9E9E9E", altezza: 55, clic: (s, e) => Chiudi(null)), 0, 0);
            azioni.Add(Ui.Tasto("OK", "#4CAF50", altezza: 55, clic: (s, e) => Conferma()), 1, 0);
            var corpo = new VerticalStackLayout
            {
                Spacing = 18, Padding = new Thickness(25),
                Children =
                {
                    new Label { Text = titolo, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#37474F"), HorizontalOptions = LayoutOptions.Center },
                    Ui.Riquadro(_campo, "#FAFAFA", 8, new Thickness(10, 4), "#B0BEC5", 1),
                    azioni
                }
            };
            var riquadro = new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 40, 0, 0),
                BackgroundColor = Colors.White, StrokeShape = new RoundRectangle { CornerRadius = 12 }, StrokeThickness = 0,
                Content = corpo
            };
            Content = riquadro;
            Dimensiona(riquadro, 520);
            Loaded += (s, e) => _campo.Focus();
        }

        private bool _conferma;

        /// <summary>Prima si chiude la tastiera (consegna la parola in scrittura), poi si legge il testo.</summary>
        private async void Conferma()
        {
            if (_conferma) return;
            _conferma = true;
            try
            {
                await Ui.ChiudiTastieraAsync(this);
                var t = (_campo.Text ?? "").Trim();
                if (t.Length == 0) return;
                Chiudi(t);
            }
            finally { _conferma = false; }
        }

        public override async void Indietro() { await Ui.ChiudiTastieraAsync(this); Chiudi(null); }

        public static Task<string> Chiedi(string titolo, string valore = "", int lunghezzaMax = 30, bool codice = false)
            => Dialogo.Mostra(new TestoBox(titolo, valore, lunghezzaMax, codice));
    }
}
