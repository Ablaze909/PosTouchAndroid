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
            var t = StileFinestre.T;
            _campo.BackgroundColor = Colors.Transparent;
            _campo.TextColor = t.Testo;
            _campo.FontSize = 20;
            var (tit, dettaglio) = StileFinestre.Dividi(titolo);
            var corpo = new VerticalStackLayout { Spacing = 12 };
            corpo.Add(StileFinestre.Intestazione(StileFinestre.IconaPer(tit), tit, dettaglio, () => Indietro()));
            corpo.Add(new Border
            {
                BackgroundColor = t.Campo, Stroke = new SolidColorBrush(Ui.C("#00897B")), StrokeThickness = 2,
                StrokeShape = new RoundRectangle { CornerRadius = 12 }, Padding = new Thickness(10, 0), Content = _campo
            });
            corpo.Add(StileFinestre.Azioni(StileFinestre.TastoSecondario("ANNULLA", (s, e) => Indietro()),
                                           StileFinestre.TastoPrincipale("OK", (s, e) => Conferma())));
            // in alto (non al centro): sotto si apre la tastiera di Android
            var riquadro = StileFinestre.Scheda(corpo);
            riquadro.VerticalOptions = LayoutOptions.Start;
            riquadro.Margin = new Thickness(0, 24, 0, 0);
            Content = riquadro;
            Dimensiona(riquadro, 480);
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
