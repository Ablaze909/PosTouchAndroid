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
    public class TavoliPage : PaginaPos
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

            var infoBar = new Grid { BackgroundColor = Ui.C("#ECEFF1"), Padding = new Thickness(12, 4) };
            infoBar.Add(_info);

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

        private void Aggiorna()
        {
            CostruisciSchede();
            CostruisciTavoli();
        }

        // ══════════════ barra schede (CostruisciTabBar del PC) ══════════════

        private void CostruisciSchede()
        {
            _schede.Children.Clear();
            _schede.ColumnDefinitions.Clear();
            var schede = new List<Button>();
            int i = 0;
            foreach (var sala in Sale())
            {
                bool attiva = sala.Id == _salaCorrente;
                string colore = sala.Id == ConfigSale.SalaConti ? ColoreConti : ColoriSale[i++ % ColoriSale.Length];
                bool chiaro = attiva;   // stessi colori della banda "dove sono" in vendita
                int id = sala.Id;
                schede.Add(Scheda(sala.Id == ConfigSale.SalaConti ? "CONTI" : sala.Nome, attiva ? colore : "#E0E0E0", chiaro,
                                  () => { _salaCorrente = _ultimaSala = id; Aggiorna(); }));
            }
            schede.Add(Scheda("Banco", "#E0E0E0", false, async () => await Navigazione.Chiudi(this)));
            if (Cfg.AbilitaComande)
            {
                int n = ContestoCassa.Spooler.InErrore;
                bool deviata = ContestoCassa.Spooler.Deviazioni().Count > 0;
                _coda = Scheda((n > 0 ? $"🖨️ CODA ({n})" : "🖨️ CODA") + (deviata ? " 🔀" : ""), n > 0 ? "#C62828" : "#37474F", true,
                               async () => { await CodaStampaBox.Mostra(); Aggiorna(); });
                schede.Add(_coda);
            }
            for (int k = 0; k < schede.Count; k++)
            {
                _schede.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                _schede.Add(schede[k], k, 0);
            }
        }

        private static Button Scheda(string testo, string sfondo, bool testoChiaro, Action clic)
        {
            var b = Ui.Tasto(testo, sfondo, testoChiaro ? "#FFFFFF" : "#424242", 16, 0, altezza: 48);
            b.Padding = new Thickness(4, 0);
            b.LineBreakMode = LineBreakMode.TailTruncation;
            b.Clicked += (s, e) => clic();
            return b;
        }

        /// <summary>Le sale (se abilitate) e poi i CONTI clienti (sala 0), come la barra del PC.</summary>
        private List<Sala> Sale()
        {
            var l = Cfg.AbilitaSale ? Cfg.Sale.ToList() : new List<Sala>();
            if (Cfg.AbilitaContiClienti)
                l.Add(new Sala { Id = ConfigSale.SalaConti, Nome = "CONTI", NumeroTavoli = Math.Clamp(Cfg.NumeroSlotContiClienti, 1, 200), NumeroIniziale = 1 });
            return l;
        }

        // ══════════════ griglia tavoli (CaricaGrigliaSlot del PC) ══════════════

        private string _firmaTavoli;

        private void CostruisciTavoli()
        {
            var sala = Sale().FirstOrDefault(x => x.Id == _salaCorrente);
            if (sala == null) { _griglia.Children.Clear(); _firmaTavoli = null; _info.Text = "Nessuna sala configurata: IMPOSTAZIONI → SALE E COMANDE"; return; }
            var aperti = ContestoCassa.Tavoli.Aperti().Where(t => t.SalaId == sala.Id && t.Occupato).ToDictionary(t => t.Numero);
            var errori = ContestoCassa.Tavoli.TavoliConErroreStampa();

            // aggiornamento ogni 10 s: si ridisegna solo se qualcosa è cambiato davvero (prestazioni)
            string firma = $"{sala.Id}|{sala.NumeroIniziale}|{sala.NumeroTavoli}|{string.Join(",", errori)}|" +
                           string.Join(";", aperti.Values.OrderBy(t => t.Numero).Select(t =>
                               $"{t.Numero}:{t.TotaleCent}:{t.Preconto}:{t.PortataCorrente}:{t.PortataMassima}:{t.DaComandare}:{t.StorniDaInviare}:{t.NomeCliente}"));
            if (firma == _firmaTavoli && _griglia.Children.Count > 0) return;
            _firmaTavoli = firma;

            _griglia.BatchBegin();
            _griglia.Children.Clear();
            for (int k = 0; k < Math.Max(1, sala.NumeroTavoli); k++)
            {
                int numero = sala.NumeroIniziale + k;
                aperti.TryGetValue(numero, out var t);
                bool errore = t != null && errori.Contains($"{sala.Id}:{numero}");
                _griglia.Add(sala.Id == ConfigSale.SalaConti ? SlotConto(sala, numero, t) : SlotTavolo(sala, numero, t, errore));
            }
            _griglia.BatchCommit();
            _info.Text = sala.Id == ConfigSale.SalaConti
                ? $"Conti sospesi — {aperti.Count} / {sala.NumeroTavoli} occupati"
                : $"{sala.Nome} — {aperti.Count} / {sala.NumeroTavoli} tavoli occupati";
        }

        private static string Euro(int cent) => $"€ {cent / 100m:N2}";
        private static Label Testo(string t, double size, string colore, bool grassetto = false, double sopra = 0) => new Label
        {
            Text = t, FontSize = size, TextColor = Ui.C(colore), FontAttributes = grassetto ? FontAttributes.Bold : FontAttributes.None,
            HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation, Margin = new Thickness(0, sopra, 0, 0)
        };

        /// <summary>Tavolo di una sala: icona tavolo, "Tav.N", totale, stato portate.</summary>
        private View SlotTavolo(Sala sala, int numero, TavoloAperto t, bool erroreStampa)
        {
            bool occupato = t != null;
            bool inCorso = occupato && t.PortataMassima > 0 && t.PortataCorrente < t.PortataMassima;
            bool completate = occupato && t.PortataMassima > 0 && t.PortataCorrente >= t.PortataMassima;
            var stack = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
            string sfondo;

            if (erroreStampa)
            {
                sfondo = "#212121";
                stack.Add(Testo($"Tav.{numero}", 15, "#FFFFFF", true));
                stack.Add(Testo("⚠ ERRORE", 14, "#FF5252", true, 4));
                stack.Add(Testo("STAMPA", 13, "#FF8A80", true));
                stack.Add(Testo(Euro(t.TotaleCent), 11, "#BDBDBD", false, 2));
                var g = new Grid();
                g.Add(new BoxView { Color = Ui.C("#D32F2F"), HeightRequest = 6, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 8, 0, 0) });
                g.Add(new BoxView { Color = Ui.C("#D32F2F"), HeightRequest = 6, VerticalOptions = LayoutOptions.End, Margin = new Thickness(0, 0, 0, 8) });
                g.Add(stack);
                return Slot(g, sfondo, t, () => ApriAsync(sala, numero));
            }

            if (!occupato) sfondo = "#F5F5F5";
            else if (t.Preconto) sfondo = "#FDD835";
            else if (inCorso) sfondo = "#EF5350";
            else sfondo = "#66BB6A";

            stack.Add(IconaTavolo(occupato));
            stack.Add(Testo($"Tav.{numero}", 15, !occupato ? "#90A4AE" : t.Preconto ? "#333333" : "#FFFFFF", true, 4));
            if (occupato)
            {
                stack.Add(Testo(t.Preconto ? $"📋 {Euro(t.TotaleCent)}" : Euro(t.TotaleCent), 13, t.Preconto ? "#333333" : "#E8F5E9", true, 2));
                string stato = t.Preconto ? "PRECONTO" : inCorso ? $"⏩ {t.PortataCorrente}/{t.PortataMassima}" : completate ? "✅ PORTATE OK" : t.Apertura.ToString("dd/MM HH:mm");
                stack.Add(Testo(stato, 10, t.Preconto ? "#E65100" : inCorso ? "#FFFFFF" : "#C8E6C9", t.Preconto || t.PortataMassima > 0));
            }
            else stack.Add(Testo("libero", 11, "#BDBDBD", false, 2));
            return Slot(stack, sfondo, t, () => ApriAsync(sala, numero));
        }

        /// <summary>Conto cliente sospeso: 🧑/👤, nome, totale.</summary>
        private View SlotConto(Sala sala, int numero, TavoloAperto t)
        {
            bool occupato = t != null;
            var stack = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
            stack.Add(new Label { Text = occupato ? "🧑" : "👤", FontSize = 36, HorizontalOptions = LayoutOptions.Center, Opacity = occupato ? 1.0 : 0.4 });
            if (occupato)
            {
                string nome = string.IsNullOrWhiteSpace(t.NomeCliente) ? $"Cliente {numero}" : t.NomeCliente;
                if (nome.Length > 12) nome = nome[..12] + "…";
                stack.Add(Testo(nome, 13, t.Preconto ? "#333333" : "#1B5E20", true, 2));
                stack.Add(Testo(t.Preconto ? $"📋 {Euro(t.TotaleCent)}" : Euro(t.TotaleCent), 14, t.Preconto ? "#333333" : "#2E7D32", true, 2));
                stack.Add(Testo(t.Preconto ? "PRECONTO" : t.Apertura.ToString("dd/MM HH:mm"), 10, t.Preconto ? "#E65100" : "#4CAF50", t.Preconto));
            }
            else
            {
                stack.Add(Testo($"Cliente {numero}", 12, "#BDBDBD", false, 3));
                stack.Add(Testo("vuoto", 11, "#BDBDBD"));
            }
            string sfondo = !occupato ? "#F5F5F5" : t.Preconto ? "#FDD835" : "#A5D6A7";
            return Slot(stack, sfondo, t, () => ApriAsync(sala, numero));
        }

        /// <summary>Riquadro 130×130 angoli 12 (PC). Bordo arancio = articoli ancora da comandare.</summary>
        private static View Slot(View contenuto, string sfondo, TavoloAperto t, Func<Task> apri)
        {
            var b = new Border
            {
                WidthRequest = 130, HeightRequest = 130, Margin = new Thickness(6),
                BackgroundColor = Ui.C(sfondo),
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Stroke = new SolidColorBrush(Ui.C(t?.DaComandare == true || t?.StorniDaInviare == true ? "#FF6F00" : sfondo)),
                StrokeThickness = t?.DaComandare == true || t?.StorniDaInviare == true ? 4 : 0,
                Content = contenuto
            };
            Ui.Tocco(b, async () => { b.Opacity = 0.8; try { await apri(); } finally { b.Opacity = 1; } });
            return b;
        }

        /// <summary>Icona tavolo disegnata come sul PC (CreaIconaTavolo): piano + 2 gambe + piedini, 40×32.</summary>
        private static View IconaTavolo(bool occupato)
        {
            var c = Ui.C(occupato ? "#FFFFFF" : "#BDBDBD");
            var a = new AbsoluteLayout { WidthRequest = 40, HeightRequest = 32, HorizontalOptions = LayoutOptions.Center };
            void R(double x, double y, double w, double h, double raggio)
            {
                var r = new Border { BackgroundColor = c, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = raggio } };
                AbsoluteLayout.SetLayoutBounds(r, new Rect(x, y, w, h));
                a.Add(r);
            }
            R(1, 6, 38, 6, 2);    // piano
            R(5, 12, 4, 16, 0);   // gamba sinistra
            R(31, 12, 4, 16, 0);  // gamba destra
            R(3, 28, 8, 3, 1);    // piedino sinistro
            R(29, 28, 8, 3, 1);   // piedino destro
            return a;
        }

        private async Task ApriAsync(Sala sala, int numero)
        {
            if (_apertura) return;
            _apertura = true;
            try
            {
                // conto cliente libero: chiedi il nome (come il PC)
                string nome = null;
                if (sala.Id == ConfigSale.SalaConti && !ContestoCassa.Tavoli.Aperti().Any(t => t.SalaId == sala.Id && t.Numero == numero && t.Occupato))
                {
                    nome = await TestoBox.Chiedi("👤 NOME CLIENTE");
                    if (string.IsNullOrWhiteSpace(nome)) return;
                }
                // prima torna alla vendita: se la mappa non si chiude (navigazione in corso) il tavolo NON si apre
                if (!await Navigazione.Chiudi(this)) return;
                SessioneTavolo s;
                try { s = ContestoCassa.ApriTavolo(sala.Id, numero, nome?.ToUpperInvariant()); }
                catch (Exception ex) { await MessaggioBox.Errore("TAVOLO", $"Impossibile aprire il tavolo:\n{ex.Message}"); return; }
                await ContestoCassa.PaginaVendita.ApriTavoloAsync(s);
            }
            finally { _apertura = false; }
        }
    }
}
