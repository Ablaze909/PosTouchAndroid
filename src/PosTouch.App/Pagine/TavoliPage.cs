using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Sale;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// Mappa tavoli (vista sale del PC): schede delle sale + BANCO + 🖨️ CODA, barra occupazione, tavoli colorati.
    /// Libero grigio, occupato verde, preconto giallo, portate in corso rosso, da comandare con bordo arancio.
    /// </summary>
    public class TavoliPage : PaginaPos
    {
        private readonly HorizontalStackLayout _schede = new HorizontalStackLayout { Spacing = 4 };
        private readonly FlexLayout _griglia = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignContent = Microsoft.Maui.Layouts.FlexAlignContent.Start };
        private readonly Label _info = new Label { FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#37474F"), VerticalOptions = LayoutOptions.Center };
        private readonly Button _coda;
        private int _salaCorrente;
        private static int _ultimaSala;
        private bool _apertura;
        private IDispatcherTimer _aggiorna;

        private ConfigSale Cfg => ContestoCassa.Impostazioni.Sale;

        public TavoliPage()
        {
            BackgroundColor = Ui.C("#ECEFF1");
            var banco = Ui.Tasto("⬅ BANCO", "#37474F", fontSize: 15, raggio: 0, clic: async (s, e) => await Navigazione.Chiudi(this), altezza: 48);
            banco.WidthRequest = 130;
            _coda = Ui.Tasto("🖨️ CODA", "#37474F", fontSize: 14, raggio: 0, clic: async (s, e) => { await CodaStampaBox.Mostra(); Aggiorna(); }, altezza: 48);
            _coda.WidthRequest = 140;

            var barra = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, BackgroundColor = Ui.C("#263238"), Padding = new Thickness(4) };
            barra.Add(banco, 0, 0);
            barra.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _schede, Margin = new Thickness(8, 0) }, 1, 0);
            barra.Add(_coda, 2, 0);

            var infoBar = new Grid { BackgroundColor = Ui.C("#CFD8DC"), Padding = new Thickness(14, 6) };
            infoBar.Add(_info);

            var radice = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) } };
            radice.Add(barra, 0, 0);
            radice.Add(infoBar, 0, 1);
            radice.Add(new ScrollView { Content = _griglia, Padding = new Thickness(8) }, 0, 2);
            Content = radice;

            _salaCorrente = Cfg.Sale.Any(s => s.Id == _ultimaSala) ? _ultimaSala : Cfg.Sale.FirstOrDefault()?.Id ?? 0;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Aggiorna();
            _aggiorna ??= Dispatcher.CreateTimer();
            _aggiorna.Interval = TimeSpan.FromSeconds(10);   // la coda e i tavoli possono cambiare (spooler)
            _aggiorna.Tick -= OnTick; _aggiorna.Tick += OnTick;
            _aggiorna.Start();
            ContestoCassa.Spooler.Cambiato += OnSpooler;
        }

        private void OnSpooler(int n) => MainThread.BeginInvokeOnMainThread(AggiornaCoda);

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _aggiorna?.Stop();
            ContestoCassa.Spooler.Cambiato -= OnSpooler;
        }

        private void OnTick(object s, EventArgs e) => AggiornaCoda();

        protected override bool Indietro()
        {
            _ = Navigazione.Chiudi(this);
            return true;
        }

        private void Aggiorna()
        {
            CostruisciSchede();
            CostruisciTavoli();
            AggiornaCoda();
        }

        private void AggiornaCoda()
        {
            int n = ContestoCassa.Spooler.InErrore;
            _coda.Text = n > 0 ? $"🖨️ CODA ⚠️ {n}" : "🖨️ CODA";
            _coda.BackgroundColor = Ui.C(n > 0 ? "#C62828" : "#37474F");
        }

        private void CostruisciSchede()
        {
            _schede.Children.Clear();
            string[] colori = { "#26A69A", "#5C6BC0", "#AB47BC", "#FFA726", "#8D6E63", "#EC407A" };
            int i = 0;
            foreach (var sala in Cfg.Sale)
            {
                bool attiva = sala.Id == _salaCorrente;
                var b = Ui.Tasto(sala.Nome, attiva ? colori[i % colori.Length] : "#455A64", attiva ? "#FFFFFF" : "#CFD8DC", 15, 0, altezza: 48);
                b.Padding = new Thickness(18, 0);
                int id = sala.Id;
                b.Clicked += (s, e) => { _salaCorrente = _ultimaSala = id; CostruisciSchede(); CostruisciTavoli(); };
                _schede.Add(b);
                i++;
            }
        }

        private void CostruisciTavoli()
        {
            _griglia.Children.Clear();
            var sala = Cfg.SalaDa(_salaCorrente);
            if (sala == null) { _info.Text = "Nessuna sala configurata: IMPOSTAZIONI → SALE"; return; }
            var aperti = ContestoCassa.Tavoli.Aperti().Where(t => t.SalaId == sala.Id && t.Articoli > 0).ToDictionary(t => t.Numero);
            int occupati = 0, totale = 0;
            for (int k = 0; k < Math.Max(0, sala.NumeroTavoli); k++)
            {
                int numero = sala.NumeroIniziale + k;
                aperti.TryGetValue(numero, out var t);
                if (t != null) { occupati++; totale += t.TotaleCent; }
                _griglia.Add(Tessera(sala, numero, t));
            }
            _info.Text = $"{sala.Nome.ToUpperInvariant()}   —   occupati {occupati}/{sala.NumeroTavoli}   —   € {totale / 100m:N2}";
        }

        /// <summary>Tasto tavolo con i colori del PC.</summary>
        private View Tessera(Sala sala, int numero, TavoloAperto t)
        {
            string sfondo, testo = "#FFFFFF", riga2, riga3;
            if (t == null) { sfondo = "#F5F5F5"; testo = "#90A4AE"; riga2 = "libero"; riga3 = ""; }
            else
            {
                string tot = $"€ {t.TotaleCent / 100m:N2}";
                if (t.Preconto) { sfondo = "#FDD835"; testo = "#333333"; riga2 = $"📋 {tot}"; riga3 = "PRECONTO"; }
                else if (t.PortataMassima > 0 && t.PortataCorrente < t.PortataMassima) { sfondo = "#EF5350"; riga2 = tot; riga3 = $"Portata {t.PortataCorrente}/{t.PortataMassima}"; }
                else { sfondo = "#66BB6A"; riga2 = tot; riga3 = t.Apertura.ToString("HH:mm"); }
            }
            var stack = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 2 };
            stack.Add(new Label { Text = numero.ToString(), FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Ui.C(testo), HorizontalOptions = LayoutOptions.Center });
            stack.Add(new Label { Text = riga2, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Ui.C(testo), HorizontalOptions = LayoutOptions.Center });
            if (riga3.Length > 0) stack.Add(new Label { Text = riga3, FontSize = 11, TextColor = Ui.C(testo), HorizontalOptions = LayoutOptions.Center });

            var tessera = new Border
            {
                WidthRequest = 124, HeightRequest = 104, Margin = new Thickness(5),
                BackgroundColor = Ui.C(sfondo),
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Stroke = new SolidColorBrush(Ui.C(t?.DaComandare == true ? "#FF6F00" : "#B0BEC5")),
                StrokeThickness = t?.DaComandare == true ? 4 : 1,
                Content = stack
            };
            Ui.Tocco(tessera, async () => await ApriAsync(sala, numero));
            return tessera;
        }

        private async Task ApriAsync(Sala sala, int numero)
        {
            if (_apertura) return;
            _apertura = true;
            try
            {
                SessioneTavolo s;
                try { s = ContestoCassa.ApriTavolo(sala.Id, numero); }
                catch (Exception ex) { await MessaggioBox.Errore("TAVOLO", $"Impossibile aprire il tavolo:\n{ex.Message}"); return; }
                await Navigazione.Chiudi(this);                       // torna alla vendita…
                await ContestoCassa.PaginaVendita.ApriTavoloAsync(s);  // …che mostra il tavolo
            }
            finally { _apertura = false; }
        }
    }
}
