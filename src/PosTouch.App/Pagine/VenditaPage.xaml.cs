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
        /// <summary>Schermo basso (8"): la riga del posto nel totale compare solo se serve (resto o cifre battute).</summary>
        private bool _compatto;
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
            RicordaOriginali();   // scritte e colori originali dei tasti in basso (servono per tornare al tema Classico)
            CollegaCarrello(null);
            _tastierino.Cambiato += () =>
            {
                DisplayTastierino.Text = _tastierino.Display;
                DisplayTastierino.TextColor = Ui.C(_tastierino.ColoreDisplay);
                DisplayTastierino.IsVisible = !string.IsNullOrEmpty(_tastierino.Display);   // riquadro bianco leggibile sul totale colorato
                if (DisplayTastierino.IsVisible) NascondiResto();   // si batte il prossimo: il resto di prima non serve più
                AggiornaRigaPosto();
                // il testo deve entrare tutto nella riga: più è lungo, più il carattere si rimpicciolisce (mai cifre tagliate)
                double spazio = Math.Max(120, RiquadroTotale.Width - 110);
                int lettere = Math.Max(1, _tastierino.Display.Length);
                DisplayTastierino.FontSize = Math.Clamp(spazio / (lettere * 0.6), 11, 17);
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
            ReteLocale.Cambiata += AggiornaAvvisoRete;
            AggiornaAvvisoRete();
            _tastierino.BloccoModificaQuantita = Cfg.BloccoModificaQuantita;
            _tastierino.VirgolaImplicita = Cfg.VirgolaImplicita;
            BtnStrumenti.IsVisible = Cfg.MostraStrumentiVendita;
            BtnConto.IsVisible = Cfg.AbilitaStampanteTermica;   // come sul PC: CONTO solo con la termica abilitata
            ApplicaTemaBarre();   // aspetto scelto in Impostazioni (si vede subito tornando alla vendita)
            AggiornaModoTavolo();
            SistemaRigaTasti();
            // Ridisegna categorie e articoli solo se catalogo o impostazioni sono cambiati
            // (OnAppearing scatta anche alla chiusura di ogni finestra: niente ridisegni inutili)
            var versione = (ContestoCassa.Catalogo.Versione, ContestoCassa.VersioneImpostazioni);
            if (versione != _versioneMostrata)
            {
                _versioneMostrata = versione;
                CaricaCategorie();
            }
            LettoreBarcode.Destinatario = AggiungiDaBarcode;
            AggiornaOperatore();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _orologio.Stop();
            ReteLocale.Cambiata -= AggiornaAvvisoRete;
            LettoreBarcode.Destinatario = null;
        }

        private void AggiornaOrologio()
        {
            // ore e minuti (senza secondi): lo schermo si ridisegna solo quando cambia il minuto
            var ora = DateTime.Now;
            string o = ora.ToString("HH:mm"), d = ora.ToString("dd/MM/yyyy");
            if (Orologio.Text != o) Orologio.Text = o;
            if (Data.Text != d) Data.Text = d;
        }

        private void AggiornaAvvisoRete() => AvvisoRete.IsVisible = !ReteLocale.Collegata;

        private double _larghezzaTasti;

        /// <summary>
        /// Barra unica sotto la tastiera: ＋ ﹣ (se attivi) · SCONTO (se attivo) · ANNULLA · 🔀 🔗 (al tavolo).
        /// Con la lista allungata restano solo ＋ ﹣ 🔀 🔗: niente SCONTO e ANNULLA.
        /// </summary>
        private void SistemaRigaTasti()
        {
            bool piuMeno = Cfg.MostraRigaPiuMeno;
            BtnVarPiu.IsVisible = BtnVarMeno.IsVisible = piuMeno;   // impostazione "tasti VAR＋ VAR﹣"
            BtnSconto.IsVisible = Cfg.AbilitaSconti && !_scontrinoEspanso;
            BtnAnnulla.IsVisible = !_scontrinoEspanso;
            var tasti = new[] { BtnVarPiu, BtnVarMeno, BtnSconto, BtnAnnulla, BtnSposta, BtnUnisci };
            RigaSconti.ColumnDefinitions.Clear();
            int col = 0;
            bool stacco = piuMeno;   // piccolo stacco tra VAR＋ VAR﹣ e il resto della barra
            foreach (var b in tasti)
            {
                if (!b.IsVisible) continue;
                bool largo = b == BtnSconto || b == BtnAnnulla || b == BtnVarPiu || b == BtnVarMeno;
                if (stacco && b != BtnVarPiu && b != BtnVarMeno)
                {
                    RigaSconti.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(4)));
                    col++;
                    stacco = false;
                }
                // 🔀 🔗 hanno solo l'icona: larghezza fissa da dito, il resto della barra ai tasti con scritta
                bool icona = b == BtnSposta || b == BtnUnisci;
                RigaSconti.ColumnDefinitions.Add(new ColumnDefinition(icona ? new GridLength(44) : new GridLength(largo ? 1.7 : 1, GridUnitType.Star)));
                Grid.SetColumn(b, col++);
            }
            RigaSconti.IsVisible = col > 0;
            // tasti stretti: icona sopra, scritta sotto
            int fissi = tasti.Count(x => x.IsVisible && (x == BtnSposta || x == BtnUnisci));
            int conScritta = tasti.Count(x => x.IsVisible && x != BtnSposta && x != BtnUnisci);
            double unita = _larghezzaTasti > 0 && conScritta > 0 ? (_larghezzaTasti - 44 * fissi) / (1.7 * conScritta) : 100;
            bool dueRighe = unita * 1.7 < 110;
            BtnSconto.Text = dueRighe ? "% €\nSCONTO" : "% € SCONTO";
            string cestino = Tema == Classico ? "🗑️" : "";
            BtnAnnulla.Text = (cestino + (dueRighe && cestino.Length > 0 ? "\n" : " ") + "ANNULLA").Trim();
            BtnVarPiu.Text = dueRighe ? "VAR\n＋" : "VAR ＋";
            BtnVarMeno.Text = dueRighe ? "VAR\n﹣" : "VAR ﹣";
        }

        // ══════════════ ADATTAMENTO ALLO SCHERMO ══════════════

        /// <summary>
        /// Stesse proporzioni del PC su qualsiasi tablet (da 8" in su): colonne, tastiera e barra
        /// si ridimensionano con lo schermo; i tasti articolo si allargano per riempire la riga.
        /// </summary>
        /// <summary>Riga alta del totale (posto / resto / cifre battute): su schermi bassi solo quando serve.</summary>
        private void AggiornaRigaPosto()
        {
            bool serve = !_compatto || BarraResto.IsVisible || DisplayTastierino.IsVisible;
            if (RigaPosto.IsVisible != serve) RigaPosto.IsVisible = serve;
            TotalePosto.IsVisible = !BarraResto.IsVisible;
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width <= 0 || height <= 0) return;

            double sinistra = Math.Clamp(width * 0.095, 96, 150);
            double destra = Math.Clamp(width * 0.26, 290, 420);
            Radice.ColumnDefinitions[0].Width = new GridLength(sinistra);
            Radice.ColumnDefinitions[2].Width = new GridLength(destra);
            // schermi da 10"–15": righe e totale più grandi (su 8" restano come prima)
            double fontRiga = Math.Clamp(destra / 24, 13, 17);
            Resources["FontRiga"] = fontRiga;
            Resources["FontRigaPiccola"] = fontRiga - 1;
            EtichettaTotale.FontSize = Math.Clamp(destra / 13, 24, 32);

            // 8" (meno di ~600 dp di altezza): più righe di scontrino a video
            _compatto = height < 600;
            AggiornaRigaPosto();

            double barra = Math.Clamp(height * 0.075, 50, 72);
            Radice.RowDefinitions[2].Height = new GridLength(barra);

            // tastiera: righe alte quanto lo permette lo schermo, lasciando spazio alla lista
            double riga = Math.Clamp((height - 380) / 4, 44, 72);
            foreach (var rd in TastieraNumerica.RowDefinitions) rd.Height = new GridLength(riga);
            foreach (var b in TastieraNumerica.Children.OfType<Button>())
                b.FontSize = b.Text == "€" ? riga * 0.6 : b.Text == "," ? riga * 0.55 : riga * 0.4;
            RigaSconti.HeightRequest = Math.Clamp(riga * 0.75, 44, 54);   // mai sotto i 44 dp (dito)

            // barra inferiore: testo che entra nei tasti
            _larghezzaBarra = width - destra - 12;
            FontBarraInferiore();
            SistemaBarraTavolo();
            _larghezzaTasti = destra - 8;
            SistemaRigaTasti();

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
            if (Occupata || CombinazioneAperta()) return;
            if (_sessione != null)
            {
                await MessaggioBox.Avviso("TAVOLO APERTO", "Esci dal tavolo prima di aprire gli strumenti.");
                return;
            }
            // con la gestione operatori si entra liberi: ogni voce di Strumenti chiede il suo permesso
            if (ContestoCassa.Operatori.Abilitata || await Accesso.VerificaPinAsync("🔧 STRUMENTI"))
                await Navigazione.Apri(this, () => new StrumentiPage());
        }

        private async void OnModifica(object sender, EventArgs e)
        {
            if (Occupata || CombinazioneAperta()) return;
            if (!Cfg.AbilitaModificaVendita)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            if (!_modalitaModifica && !await Accesso.VerificaAsync(Permesso.ModalitaModifica, "MODALITÀ MODIFICA")) return;
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
            if (attiva) BtnModifica.BackgroundColor = Ui.C("#FF9800");
            else TastoBanda(BtnModifica, "✏️", IcoModifica, Tema);   // spento: torna come il tema (prima grigio fisso)
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
        {
            if (Occupata) return;   // niente chiusura Z a metà di uno scontrino
            await FunzioniCassaBox.Mostra();
        }

        /// <summary>🚪: torna alla schermata iniziale (come TornaAlLauncher del PC).</summary>
        private async void OnEsci(object sender, EventArgs e)
        {
            if (CombinazioneAperta()) return;
            if (_sessione != null) { OnTornaSala(); return; }   // al tavolo 🚪 diventa ⬅ SALA
            if (!Carrello.Vuoto)
            {
                await MessaggioBox.Avviso("SCONTRINO APERTO", "Chiudi o svuota lo scontrino prima di uscire.");
                return;
            }
            if (_modalitaModifica) ImpostaModalitaModifica(false);
            // come il PC (btnEsci_Click): con sale o conti clienti attivi 🚪 porta alla schermata SALE
            var sale = ContestoCassa.Impostazioni.Sale;
            if ((sale.AbilitaSale && sale.Sale.Count > 0) || sale.AbilitaContiClienti)
            {
                await Navigazione.Apri(this, () => new TavoliPage());
                return;
            }
            await Navigazione.Chiudi(this);
        }

        /// <summary>Tasto indietro di Android: come 🚪 (non si esce con lo scontrino aperto o durante la stampa).</summary>
        protected override bool Indietro()
        {
            if (Attesa.IsVisible) return true;
            if (OverlayPagamento.IsVisible) { OverlayPagamento.IsVisible = false; return true; }   // prima chiude il popup CASSA
            if (_modalitaModifica) { ImpostaModalitaModifica(false); return true; } // indietro = esci dalla modifica
            if (_sessione != null) { OnTornaSala(); return true; }
            OnEsci(this, EventArgs.Empty);
            return true;
        }

        private static readonly HashSet<View> _premuti = new();

        /// <summary>
        /// Tasto premuto: si rimpicciolisce e si scurisce per 0,14 s, poi torna com'era.
        /// Scala e trasparenza non ridispongono la griglia: costa pochissimo anche su tablet lenti.
        /// </summary>
        private static void Premuto(View vista)
        {
            try
            {
                var d = vista.Dispatcher;
                if (d == null || !_premuti.Add(vista)) return;   // tocchi rapidi: un effetto alla volta
                double prima = vista.Opacity, premuto = prima * 0.6;
                vista.Opacity = premuto;
                vista.Scale = 0.93;
                d.DispatchDelayed(TimeSpan.FromMilliseconds(140), () =>
                {
                    _premuti.Remove(vista);
                    vista.Scale = 1;
                    // se intanto è cambiata (es. categoria appena scelta → piena) non si tocca
                    if (Math.Abs(vista.Opacity - premuto) < 0.001) vista.Opacity = prima;
                });
            }
            catch { vista.Scale = 1; }
        }

        private static void Tocco(View vista, Func<Task> azione)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) =>
            {
                Premuto(vista);   // effetto breve e fisso: si vede anche se l'azione dura pochi millesimi
                await azione();
            };
            vista.GestureRecognizers.Add(tap);
        }
    }
}
