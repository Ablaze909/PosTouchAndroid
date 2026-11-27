using PosTouch.App.Dialoghi;

namespace PosTouch.App.Controlli
{
    /// <summary>
    /// Campo di scelta nello stile POS Touch: al tocco apre una griglia di tasti (SceltaBox)
    /// al posto del menu a tendina di Android, che aprirebbe una finestra di sistema
    /// facendo ricomparire le barre di Android.
    /// </summary>
    public class CampoScelta : ContentView
    {
        private readonly Label _testo;
        private IList<string> _voci = new List<string>();
        private int _indice = -1;

        public event EventHandler Cambiato;

        public string Titolo { get; set; } = "Scegli";
        public int Colonne { get; set; } = 3;

        public static readonly BindableProperty FontSizeProperty =
            BindableProperty.Create(nameof(FontSize), typeof(double), typeof(CampoScelta), 16.0,
                propertyChanged: (b, o, n) => ((CampoScelta)b)._testo.FontSize = (double)n);
        public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

        public CampoScelta()
        {
            _testo = new Label { FontSize = 16, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
            var freccia = new Label { Text = "▾", FontSize = 18, TextColor = Ui.C("#757575"), VerticalOptions = LayoutOptions.Center };
            var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, BackgroundColor = Colors.Transparent };
            g.Add(_testo, 0, 0);
            g.Add(freccia, 1, 0);
            Content = g;
            Ui.Tocco(this, async () =>
            {
                if (_voci.Count == 0) return;
                int i = await SceltaBox.Testi(Titolo, _voci, Colonne);
                if (i >= 0 && i != _indice)
                {
                    Indice = i;
                    Cambiato?.Invoke(this, EventArgs.Empty);
                }
            });
            Aggiorna();
        }

        public IList<string> Voci
        {
            get => _voci;
            set { _voci = value ?? new List<string>(); if (_indice >= _voci.Count) _indice = -1; Aggiorna(); }
        }

        /// <summary>Indice della voce scelta (-1 = nessuna). Impostarlo da codice non genera <see cref="Cambiato"/>.</summary>
        public int Indice
        {
            get => _indice;
            set { _indice = value >= 0 && value < _voci.Count ? value : -1; Aggiorna(); }
        }

        private void Aggiorna()
        {
            _testo.Text = _indice >= 0 ? _voci[_indice] : "Tocca per scegliere";
            _testo.TextColor = StilePagine.TestoValore(_indice >= 0);   // leggibile anche col tema Scuro
        }
    }
}
