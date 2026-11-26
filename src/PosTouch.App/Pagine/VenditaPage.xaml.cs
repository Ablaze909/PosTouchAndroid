using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// Vendita al banco: stesso layout, stessi tasti e stessi gesti di MainWindow (POS Touch Windows).
    /// La logica sta in Carrello, Tastierino e ServizioVendita (libreria Core).
    /// </summary>
    public partial class VenditaPage : PaginaPos
    {
        private const int IdPreferiti = -1;
        private double _latoTasto = 100;     // 100×100 come sul PC; si adatta per riempire la riga

        private readonly Tastierino _tastierino = new Tastierino();
        private readonly IDispatcherTimer _orologio;
        private IDispatcherTimer _timerResto;
        private int _categoriaCorrente = IdPreferiti;
        private bool _modalitaModifica;
        private bool _scontrinoEspanso;
        private (int, int) _versioneMostrata = (-1, -1);

        // doppio tocco sulle righe
        private RigaScontrino _ultimaRigaToccata;
        private DateTime _ultimoTocco = DateTime.MinValue;

        /// <summary>Scontrino a video: quello del banco o quello del tavolo aperto.</summary>
        private Carrello Carrello => _sessione?.Carrello ?? ContestoCassa.Carrello;
        private Impostazioni Cfg => ContestoCassa.Impostazioni;

        public VenditaPage()
        {
            InitializeComponent();
            CollegaCarrello(null);
            _tastierino.Cambiato += () =>
            {
                DisplayTastierino.Text = _tastierino.Display;
                DisplayTastierino.TextColor = Ui.C(_tastierino.ColoreDisplay);
            };

            _orologio = Dispatcher.CreateTimer();
            _orologio.Interval = TimeSpan.FromSeconds(1);
            _orologio.Tick += (s, e) => AggiornaOrologio();
            AggiornaOrologio();
            AggiornaTotale();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _orologio.Start();
            _tastierino.BloccoModificaQuantita = Cfg.BloccoModificaQuantita;
            BtnStrumenti.IsVisible = Cfg.MostraStrumentiVendita;
            RigaQuantita.IsVisible = Cfg.MostraRigaPiuMeno;
            BtnConto.IsVisible = Cfg.AbilitaStampanteTermica;   // come sul PC: CONTO solo con la termica abilitata
            AggiornaModoTavolo();
            // Sconti disattivati: spariscono i due tasti sconto, ANNULLA resta e occupa tutta la riga
            BtnScontoValore.IsVisible = BtnScontoPerc.IsVisible = Cfg.AbilitaSconti;
            Grid.SetColumn(BtnAnnulla, Cfg.AbilitaSconti ? 2 : 0);
            Grid.SetColumnSpan(BtnAnnulla, Cfg.AbilitaSconti ? 1 : 3);
            // Ridisegna categorie e articoli solo se catalogo o impostazioni sono cambiati
            // (OnAppearing scatta anche alla chiusura di ogni finestra: niente ridisegni inutili)
            var versione = (ContestoCassa.Catalogo.Versione, ContestoCassa.VersioneImpostazioni);
            if (versione != _versioneMostrata)
            {
                _versioneMostrata = versione;
                CaricaCategorie();
            }
            LettoreBarcode.Destinatario = AggiungiDaBarcode;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _orologio.Stop();
            LettoreBarcode.Destinatario = null;
        }

        private void AggiornaOrologio()
        {
            Orologio.Text = DateTime.Now.ToString("HH:mm:ss");
            Data.Text = DateTime.Now.ToString("dd/MM/yyyy");
        }

        // ══════════════ ADATTAMENTO ALLO SCHERMO ══════════════

        /// <summary>
        /// Stesse proporzioni del PC su qualsiasi tablet (da 8" in su): colonne, tastiera e barra
        /// si ridimensionano con lo schermo; i tasti articolo si allargano per riempire la riga.
        /// </summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width <= 0 || height <= 0) return;

            double sinistra = Math.Clamp(width * 0.095, 96, 150);
            double destra = Math.Clamp(width * 0.26, 290, 420);
            Radice.ColumnDefinitions[0].Width = new GridLength(sinistra);
            Radice.ColumnDefinitions[2].Width = new GridLength(destra);

            double barra = Math.Clamp(height * 0.075, 50, 72);
            Radice.RowDefinitions[1].Height = new GridLength(barra);

            // tastiera: righe alte quanto lo permette lo schermo, lasciando spazio alla lista
            double riga = Math.Clamp((height - 380) / 4, 44, 72);
            foreach (var rd in TastieraNumerica.RowDefinitions) rd.Height = new GridLength(riga);
            foreach (var b in TastieraNumerica.Children.OfType<Button>())
                b.FontSize = b.Text == "€" ? riga * 0.6 : b.Text == "," ? riga * 0.55 : riga * 0.4;
            RigaSconti.HeightRequest = RigaQuantita.HeightRequest = Math.Clamp(riga * 0.7, 36, 52);

            // barra inferiore: testo che entra nei tasti
            double colonna = (width - destra - 12) / 5;
            foreach (var b in BarraInferiore.Children.OfType<Button>())
                b.FontSize = Math.Clamp(colonna / 7.5, 12, 20);

            // tasti articolo: quanti ne entrano in una riga, poi allargati per riempirla
            double area = width - sinistra - destra - 8;
            int perRiga = Math.Max(3, (int)(area / 110));
            double lato = Math.Clamp(Math.Floor(area / perRiga) - 6, 90, 150);
            if (Math.Abs(lato - _latoTasto) > 1)
            {
                _latoTasto = lato;
                MostraArticoli();
            }
        }

        // ══════════════ TOOLBAR ══════════════

        private async void OnStrumenti(object sender, EventArgs e)
        {
            if (await Accesso.VerificaPinAsync("🔧 STRUMENTI"))
                await Navigazione.Apri(this, () => new StrumentiPage());
        }

        private async void OnModifica(object sender, EventArgs e)
        {
            if (!Cfg.AbilitaModificaVendita)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            ImpostaModalitaModifica(!_modalitaModifica);
        }

        /// <summary>
        /// Modalità modifica: si possono solo scegliere categorie e toccare articoli per modificarli.
        /// Scontrino, tastiera, sconti, CASSA/CONTO/CASSETTO/BARCODE, lettore barcode, strumenti,
        /// funzioni cassa e uscita restano bloccati finché non si preme di nuovo ✏️.
        /// </summary>
        private void ImpostaModalitaModifica(bool attiva)
        {
            _modalitaModifica = attiva;
            BtnModifica.BackgroundColor = Ui.C(attiva ? "#FF9800" : "#757575");
            OverlayModifica.IsVisible = attiva;
            VeloScontrino.IsVisible = VeloBarra.IsVisible = attiva;
            BtnEsci.IsEnabled = BtnStrumenti.IsEnabled = BtnFunzioniCassa.IsEnabled = !attiva;
            BtnEsci.Opacity = BtnStrumenti.Opacity = BtnFunzioniCassa.Opacity = attiva ? 0.4 : 1;
            if (attiva)
            {
                _tastierino.Azzera();
                OverlayPagamento.IsVisible = false;
                NascondiResto();
            }
        }

        private async void OnFunzioniCassa(object sender, EventArgs e)
            => await FunzioniCassaBox.Mostra();

        /// <summary>🚪: torna alla schermata iniziale (come TornaAlLauncher del PC).</summary>
        private async void OnEsci(object sender, EventArgs e)
        {
            if (_sessione != null) { OnTornaSala(); return; }   // al tavolo 🚪 diventa ⬅ SALA
            if (!Carrello.Vuoto)
            {
                await MessaggioBox.Avviso("SCONTRINO APERTO", "Chiudi o svuota lo scontrino prima di uscire.");
                return;
            }
            await Navigazione.Chiudi(this);
        }

        /// <summary>Tasto indietro di Android: come 🚪 (non si esce con lo scontrino aperto o durante la stampa).</summary>
        protected override bool Indietro()
        {
            if (Attesa.IsVisible) return true;
            if (_sessione != null && !_modalitaModifica) { OnTornaSala(); return true; }
            if (_modalitaModifica) { ImpostaModalitaModifica(false); return true; } // indietro = esci dalla modifica
            if (OverlayPagamento.IsVisible) { OverlayPagamento.IsVisible = false; return true; }
            OnEsci(this, EventArgs.Empty);
            return true;
        }

        private static void Tocco(View vista, Func<Task> azione)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) =>
            {
                vista.Opacity = 0.75;
                try { await azione(); }
                finally { vista.Opacity = 1; }
            };
            vista.GestureRecognizers.Add(tap);
        }
    }
}
