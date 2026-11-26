using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.App.Servizi;
using PosTouch.Core.Sale;

namespace PosTouch.App.Dialoghi
{
    /// <summary>
    /// Scelta dei tavoli per 🔀 SPOSTA (uno: libero = sposta, occupato = scambio) e 🔗 UNISCI (più tavoli occupati),
    /// con le schede delle sale e la legenda colori del PC.
    /// </summary>
    public class SceltaTavoloBox : DialogoBase<object>
    {
        private readonly bool _unisci;
        private readonly int _salaCorrente, _numeroCorrente;
        private int _salaVista;
        private readonly HorizontalStackLayout _schede = new HorizontalStackLayout { Spacing = 4 };
        private readonly FlexLayout _griglia = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        private readonly Label _contatore = new Label { FontSize = 14, TextColor = Ui.C("#607D8B"), HorizontalOptions = LayoutOptions.Center };
        private readonly Dictionary<long, TavoloAperto> _selezionati = new Dictionary<long, TavoloAperto>();
        private readonly List<TavoloAperto> _aperti;

        private SceltaTavoloBox(bool unisci, int sala, int numero)
        {
            _unisci = unisci; _salaCorrente = _salaVista = sala; _numeroCorrente = numero;
            _aperti = ContestoCassa.Tavoli.Aperti().Where(t => t.Occupato).ToList();
            var cfg = ContestoCassa.Impostazioni.Sale;
            string nome = cfg.SalaDa(sala)?.Nome ?? "";

            var titolo = new Label
            {
                Text = unisci ? $"🔗 Seleziona i tavoli da unire nel TAVOLO {numero} ({nome}):" : $"🔀 Sposta TAVOLO {numero} ({nome}) verso:",
                FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#37474F"), HorizontalOptions = LayoutOptions.Center
            };
            var legenda = new Label
            {
                Text = unisci ? "🟧 occupato   🟩 selezionato   🟦 destinazione   ⬜ libero" : "🟩 libero   🟥 occupato (scambio)   🟦 corrente",
                FontSize = 13, TextColor = Ui.C("#607D8B"), HorizontalOptions = LayoutOptions.Center
            };
            var azioni = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
            azioni.Add(Ui.Tasto("ANNULLA", "#9E9E9E", altezza: 52, clic: (s, e) => Chiudi(null)), 0, 0);
            if (unisci)
                azioni.Add(Ui.Tasto("✅ UNISCI SELEZIONATI", "#43A047", altezza: 52, clic: async (s, e) =>
                {
                    if (_selezionati.Count == 0) { await MessaggioBox.Avviso("NESSUNA SELEZIONE", "Seleziona almeno un tavolo da unire."); return; }
                    if (!await MessaggioBox.Conferma("UNISCI CONTI", $"Unire {_selezionati.Count} tavolo/i nel TAVOLO {numero}?\nI tavoli uniti si liberano.")) return;
                    Chiudi(_selezionati.Keys.ToList());
                }), 1, 0);
            else Grid.SetColumnSpan(azioni.Children[0] as View, 2);

            var corpo = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, Padding = new Thickness(18), RowSpacing = 10 };
            corpo.Add(titolo, 0, 0);
            corpo.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _schede }, 0, 1);
            corpo.Add(new ScrollView { Content = _griglia }, 0, 2);
            corpo.Add(legenda, 0, 3);
            if (unisci) corpo.Add(_contatore, 0, 4);
            corpo.Add(azioni, 0, 5);
            var riquadro = new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Ui.C("#ECEFF1"), StrokeShape = new RoundRectangle { CornerRadius = 12 }, StrokeThickness = 0, Content = corpo
            };
            Content = riquadro;
            Dimensiona(riquadro, 760, 620);
            Disegna();
        }

        private void Disegna()
        {
            var cfg = ContestoCassa.Impostazioni.Sale;
            _schede.Children.Clear();
            foreach (var s in cfg.Sale)
            {
                int id = s.Id;
                var b = Ui.Tasto(s.Nome, id == _salaVista ? "#00897B" : "#B0BEC5", id == _salaVista ? "#FFFFFF" : "#37474F", 14, 6, (x, y) => { _salaVista = id; Disegna(); }, 44);
                b.Padding = new Thickness(16, 0);
                _schede.Add(b);
            }
            _griglia.Children.Clear();
            var sala = cfg.SalaDa(_salaVista);
            if (sala == null) return;
            for (int k = 0; k < sala.NumeroTavoli; k++)
            {
                int numero = sala.NumeroIniziale + k;
                var t = _aperti.FirstOrDefault(a => a.SalaId == sala.Id && a.Numero == numero);
                bool corrente = sala.Id == _salaCorrente && numero == _numeroCorrente;
                string colore = corrente ? "#1565C0"
                    : _unisci ? (t == null ? "#BDBDBD" : _selezionati.ContainsKey(t.Id) ? "#4CAF50" : "#FF8A65")
                    : (t == null ? "#66BB6A" : "#E57373");
                var b = Ui.Tasto($"T{numero}", colore, "#FFFFFF", 16, 6, altezza: 58);
                b.WidthRequest = 76;
                b.Margin = new Thickness(4);
                b.IsEnabled = !corrente && (!_unisci || t != null);
                int n = numero, salaId = sala.Id; string nomeSala = sala.Nome;
                b.Clicked += (x, y) =>
                {
                    if (_unisci)
                    {
                        if (!_selezionati.Remove(t.Id)) _selezionati[t.Id] = t;
                        _contatore.Text = _selezionati.Count == 0 ? "Nessun tavolo selezionato" : $"{_selezionati.Count} tavolo/i selezionato/i";
                        Disegna();
                    }
                    else Chiudi((salaId, n, $"TAVOLO {n} ({nomeSala})"));
                };
                _griglia.Add(b);
            }
        }

        public static async Task<(int sala, int numero, string nome)?> Sposta(int sala, int numero)
            => await Dialogo.Mostra(new SceltaTavoloBox(false, sala, numero)) is ValueTuple<int, int, string> r ? r : null;

        public static async Task<List<long>> Unisci(int sala, int numero)
            => await Dialogo.Mostra(new SceltaTavoloBox(true, sala, numero)) as List<long>;
    }
}
