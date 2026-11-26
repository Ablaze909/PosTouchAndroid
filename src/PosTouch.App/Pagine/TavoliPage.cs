using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Sale;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// Schermata SALE come sul PC (gridContiClienti): 🚪 rosso + barra schede [SALA…] [CONTI] [Banco] [🖨️ CODA],
    /// barra occupazione, tavoli 130×130 con l'icona del tavolo disegnata.
    /// Libero grigio, occupato verde, preconto giallo, portate in corso rosso, errore stampa nero.
    /// </summary>
    public partial class TavoliPage : PaginaPos
    {
        private static readonly string[] ColoriSale = { "#00897B", "#E07A5F", "#7CB342" };   // teal, corallo, lime (PC)
        public const string ColoreBanco = "#37474F";
        public const string ColoreConti = "#1E88E5";

        /// <summary>Colore di una sala come la sua scheda (Banco antracite, conti azzurro): lo usa anche la vendita per dire "dove sei".</summary>
        public static string ColoreDi(int salaId)
        {
            if (salaId == ConfigSale.SalaConti) return ColoreConti;
            var sale = ContestoCassa.Impostazioni.Sale.Sale;
            int i = sale.FindIndex(s => s.Id == salaId);
            return i < 0 ? ColoreBanco : ColoriSale[i % ColoriSale.Length];
        }

        private readonly Grid _schede = new Grid { ColumnSpacing = 0 };
        private readonly FlexLayout _griglia = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Center,
            AlignContent = Microsoft.Maui.Layouts.FlexAlignContent.Start
        };
        private readonly Label _info = new Label { FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#263238"), VerticalOptions = LayoutOptions.Center };
        private Button _coda;
        private int _salaCorrente;
        private static int _ultimaSala;
        private bool _apertura;
        private IDispatcherTimer _aggiorna;

        private ConfigSale Cfg => ContestoCassa.Impostazioni.Sale;

        public TavoliPage()
        {
            BackgroundColor = Ui.C("#F5F0EB");
            var esci = Ui.Tasto("🚪", "#B71C1C", fontSize: 18, raggio: 0, clic: async (s, e) => await TornaAlLauncherAsync(), altezza: 48);
            esci.WidthRequest = 50;

            var testa = new Grid { ColumnDefinitions = { new(new GridLength(50)), new(GridLength.Star) }, HeightRequest = 48 };
            testa.Add(esci, 0, 0);
            testa.Add(_schede, 1, 0);

            var infoBar = new Grid { BackgroundColor = Ui.C("#ECEFF1"), Padding = new Thickness(12, 4), ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12 };
            infoBar.Add(_info, 0, 0);
            infoBar.Add(Legenda(), 1, 0);

            var radice = new Grid { RowDefinitions = { new(new GridLength(48)), new(GridLength.Auto), new(GridLength.Star) } };
            radice.Add(testa, 0, 0);
            radice.Add(infoBar, 0, 1);
            radice.Add(new ScrollView { Content = _griglia, Margin = new Thickness(10, 6, 10, 10) }, 0, 2);
            Content = radice;

            var sale = Sale();
            _salaCorrente = sale.Any(x => x.Id == _ultimaSala) ? _ultimaSala : sale.FirstOrDefault()?.Id ?? 0;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Aggiorna();
            _aggiorna ??= Dispatcher.CreateTimer();
            _aggiorna.Interval = TimeSpan.FromSeconds(10);   // coda e tavoli possono cambiare (spooler)
            _aggiorna.Tick -= OnTick; _aggiorna.Tick += OnTick;
            _aggiorna.Start();
            ContestoCassa.Spooler.Cambiato += OnSpooler;
        }

        private void OnSpooler(int n) => MainThread.BeginInvokeOnMainThread(Aggiorna);

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _aggiorna?.Stop();
            ContestoCassa.Spooler.Cambiato -= OnSpooler;
        }

        private void OnTick(object s, EventArgs e) => Aggiorna();

        /// <summary>Tasto indietro di Android = tab Banco.</summary>
        protected override bool Indietro()
        {
            _ = Navigazione.Chiudi(this);
            return true;
        }

        /// <summary>🚪: torna alla schermata iniziale (btnTornaLauncher del PC).</summary>
        private async Task TornaAlLauncherAsync()
        {
            if (!ContestoCassa.Carrello.Vuoto)
            {
                await MessaggioBox.Avviso("SCONTRINO APERTO", "Al banco c'è uno scontrino aperto.\nVai su Banco e chiudilo o svuotalo prima di uscire.");
                return;
            }
            await Navigazione.TornaAllInizio(this);
        }

        /// <summary>Che cosa vogliono dire i colori dei tavoli (prima si doveva sapere).</summary>
        private static View Legenda()
        {
            var l = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center };
            void Voce(string colore, string testo, string bordo = null)
            {
                var quadrato = new Border
                {
                    WidthRequest = 16, HeightRequest = 16, BackgroundColor = Ui.C(colore), StrokeShape = new RoundRectangle { CornerRadius = 3 },
                    Stroke = new SolidColorBrush(Ui.C(bordo ?? colore)), StrokeThickness = bordo != null ? 3 : 0, VerticalOptions = LayoutOptions.Center
                };
                l.Add(new HorizontalStackLayout { Spacing = 4, Children = { quadrato, new Label { Text = testo, FontSize = 12, TextColor = Ui.C("#37474F"), VerticalOptions = LayoutOptions.Center } } });
            }
            Voce("#43A047", "occupato");
            Voce("#E53935", "portate in corso");
            Voce("#FDD835", "preconto");
            Voce("#ECEFF1", "da comandare", "#FF6F00");
            Voce("#212121", "errore stampa");
            return l;
        }

        /// <summary>Riquadri dei tavoli più grandi sugli schermi larghi (10"–15"), 130 su 8" come il PC.</summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width <= 0) return;
            double area = width - 20;
            int perRiga = Math.Max(4, (int)(area / 142));
            // 8" e simili: 130 come il PC (più tavoli a video); solo sugli schermi larghi crescono
            double lato = width < 1100 ? 130 : Math.Clamp(Math.Floor(area / perRiga) - 12, 130, 180);
            if (Math.Abs(lato - LatoSlot) > 2)
            {
                LatoSlot = lato;
                _firmaTavoli = null;   // ridisegna con la misura nuova
                CostruisciTavoli();
            }
        }

        private void Aggiorna()
        {
            CostruisciSchede();
            CostruisciTavoli();
        }

    }
}
