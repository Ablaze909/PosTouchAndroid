using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;

namespace PosTouch.App.Dialoghi
{
    /// <summary>
    /// "Parametri per: BAR" (Configura di POSBY): stampante scelta dall'elenco IP, righe di salto iniziali e finali,
    /// taglio carta, cicalino, inversione, controllo di stato; 🖨️ prova, copia / incolla, 💾 salva.
    /// Lavora su una copia: restituisce i nuovi parametri (null = esci senza salvare).
    /// </summary>
    public class ConfiguraStampaBox : DialogoBase<ParametriStampa>
    {
        /// <summary>Appunti di copia/incolla dei parametri (come i tasti 📋 di POSBY).</summary>
        private static ParametriStampa _appunti;

        private readonly ParametriStampa _p;
        private readonly Func<List<string>> _elencoIP;
        private readonly Func<List<string>, Task> _salvaIP;
        private readonly CampoScelta _stampante = new CampoScelta { Titolo = "Stampante", Colonne = 2, FontSize = 20 };
        private readonly Label _saltoFinale = Numero(), _saltoIniziale = Numero();
        private readonly Switch _stato = new Switch { OnColor = Ui.C("#26A69A") }, _inversione = new Switch { OnColor = Ui.C("#26A69A") };
        private readonly List<Button> _tagli = new(), _beep = new();
        private bool _prova;

        private ConfiguraStampaBox(string nome, ParametriStampa attuali, Func<List<string>> elencoIP, Func<List<string>, Task> salvaIP)
        {
            _p = (attuali ?? new ParametriStampa()).Copia();
            _elencoIP = elencoIP; _salvaIP = salvaIP;

            var g = new Grid
            {
                ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) },
                RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) },
                ColumnSpacing = 24, RowSpacing = 10, Padding = new Thickness(22, 14)
            };
            var titolo = new Label { Text = $"Parametri per: {nome}", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center };
            g.Add(titolo, 0, 0); Grid.SetColumnSpan(titolo, 2);

            // stampante + gestione IP
            var colIP = new VerticalStackLayout { Spacing = 6 };
            colIP.Add(Etichetta("Stampante (porte disponibili)"));
            colIP.Add(Ui.Riquadro(_stampante, "#F5EEE6", 4, new Thickness(10, 8)));
            g.Add(colIP, 0, 1);
            var gestione = Ui.Tasto("Gestione porte TCP/IP", "#37474F", "#ECEFF1", 16, 4, async (s, e) => await GestisciIPAsync(), 52);
            gestione.VerticalOptions = LayoutOptions.End;
            g.Add(gestione, 1, 1);

            // righe di salto
            var salti = new VerticalStackLayout { Spacing = 8 };
            salti.Add(Etichetta("Righe di salto finale"));
            salti.Add(Contatore(_saltoFinale, () => _p.RigheSaltoFinale, v => _p.RigheSaltoFinale = v));
            salti.Add(Etichetta("Righe di salto iniziale"));
            salti.Add(Contatore(_saltoIniziale, () => _p.RigheSaltoIniziale, v => _p.RigheSaltoIniziale = v));
            g.Add(salti, 0, 2);

            // taglio e cicalino
            var gruppi = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 12 };
            gruppi.Add(Gruppo("Taglio carta", new[] { "Nessuno", "Parziale", "Totale" }, _tagli, () => _p.Taglio, v => _p.Taglio = v), 0, 0);
            gruppi.Add(Gruppo("Cicalino", new[] { "Nessuno", "1 Beep", "2 Beep" }, _beep, () => _p.Cicalino, v => _p.Cicalino = v), 1, 0);
            g.Add(gruppi, 1, 2);

            // interruttori
            var spunte = new HorizontalStackLayout { Spacing = 30 };
            spunte.Add(Interruttore(_stato, "Controllo di stato"));
            spunte.Add(Interruttore(_inversione, "Inversione della stampa"));
            g.Add(spunte, 0, 3); Grid.SetColumnSpan(spunte, 2);

            // tasti
            var tasti = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10, Margin = new Thickness(0, 10, 0, 0) };
            tasti.Add(Ui.Tasto("⬅ ESCI", "#C2185B", fontSize: 15, altezza: 58, clic: (s, e) => Chiudi(null)), 0, 0);
            tasti.Add(Ui.Tasto("🖨️ PROVA", "#00897B", fontSize: 15, altezza: 58, clic: async (s, e) => await ProvaAsync()), 1, 0);
            tasti.Add(Ui.Tasto("📋 COPIA", "#546E7A", fontSize: 15, altezza: 58, clic: async (s, e) => { Leggi(); _appunti = _p.Copia(); await MessaggioBox.Info("COPIA", "Parametri copiati: aprili su un'altra stampa e premi INCOLLA."); }), 2, 0);
            tasti.Add(Ui.Tasto("📥 INCOLLA", "#546E7A", fontSize: 15, altezza: 58, clic: (s, e) => Incolla()), 3, 0);
            tasti.Add(Ui.Tasto("💾 SALVA", "#2E7D32", fontSize: 16, altezza: 58, clic: (s, e) => { Leggi(); Chiudi(_p); }), 4, 0);
            g.Add(tasti, 0, 4); Grid.SetColumnSpan(tasti, 2);

            var riquadro = new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Ui.C("#263238"), StrokeShape = new RoundRectangle { CornerRadius = 12 }, StrokeThickness = 0,
                Content = new ScrollView { Content = g }
            };
            Content = riquadro;
            Dimensiona(riquadro, 900);
            Mostra();
        }

        private static Label Numero() => new Label { FontSize = 30, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black, BackgroundColor = Colors.White, WidthRequest = 80, HeightRequest = 56, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        private static Label Etichetta(string t) => new Label { Text = t, FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };

        private View Contatore(Label valore, Func<int> leggi, Action<int> scrivi)
        {
            var r = new HorizontalStackLayout { Spacing = 8 };
            var meno = Ui.Tasto("−", "#37474F", "#FFFFFF", 26, 4, (s, e) => { scrivi(Math.Max(0, leggi() - 1)); valore.Text = leggi().ToString(); }, 56); meno.WidthRequest = 70;
            var piu = Ui.Tasto("+", "#37474F", "#FFFFFF", 26, 4, (s, e) => { scrivi(Math.Min(20, leggi() + 1)); valore.Text = leggi().ToString(); }, 56); piu.WidthRequest = 70;
            r.Add(meno); r.Add(valore); r.Add(piu);
            return r;
        }

        private View Gruppo(string titolo, string[] voci, List<Button> tasti, Func<int> leggi, Action<int> scrivi)
        {
            var v = new VerticalStackLayout { Spacing = 6 };
            v.Add(Etichetta(titolo));
            for (int i = 0; i < voci.Length; i++)
            {
                int k = i;
                var b = Ui.Tasto(voci[i], "#37474F", "#FFFFFF", 16, 6, (s, e) => { scrivi(k); Colora(tasti, leggi()); }, 50);
                tasti.Add(b);
                v.Add(b);
            }
            return v;
        }

        private static void Colora(List<Button> tasti, int scelto)
        {
            for (int i = 0; i < tasti.Count; i++) tasti[i].BackgroundColor = Ui.C(i == scelto ? "#26A69A" : "#37474F");
        }

        private static View Interruttore(Switch sw, string testo)
        {
            var r = new HorizontalStackLayout { Spacing = 6 };
            r.Add(sw);
            var l = new Label { Text = testo, FontSize = 17, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
            Ui.Tocco(l, () => sw.IsToggled = !sw.IsToggled);
            r.Add(l);
            return r;
        }

        private List<string> VociIP() => new[] { "— nessuna —" }.Concat(_elencoIP()).ToList();

        /// <summary>Riporta a video i parametri (all'apertura e dopo INCOLLA).</summary>
        private void Mostra()
        {
            var voci = VociIP();
            if (_p.Assegnata && !voci.Contains(_p.Descrizione)) voci.Add(_p.Descrizione);   // IP non più nell'elenco: resta visibile
            _stampante.Voci = voci;
            _stampante.Indice = _p.Assegnata ? voci.IndexOf(_p.Descrizione) : 0;
            _saltoFinale.Text = _p.RigheSaltoFinale.ToString();
            _saltoIniziale.Text = _p.RigheSaltoIniziale.ToString();
            _stato.IsToggled = _p.ControlloStato;
            _inversione.IsToggled = _p.Inversione;
            Colora(_tagli, _p.Taglio);
            Colora(_beep, _p.Cicalino);
        }

        /// <summary>Legge da video quello che non si aggiorna da solo (stampante e interruttori).</summary>
        private void Leggi()
        {
            int i = _stampante.Indice;
            if (i <= 0) { _p.IP = ""; _p.Porta = 9100; }
            else { var (ip, porta) = ConfigStampe.Separa(_stampante.Voci[i]); _p.IP = ip; _p.Porta = porta; }
            _p.ControlloStato = _stato.IsToggled;
            _p.Inversione = _inversione.IsToggled;
        }

        private void Incolla()
        {
            if (_appunti == null) { _ = MessaggioBox.Avviso("INCOLLA", "Prima premi COPIA su un'altra stampa."); return; }
            var a = _appunti.Copia();
            _p.IP = a.IP; _p.Porta = a.Porta; _p.RigheSaltoIniziale = a.RigheSaltoIniziale; _p.RigheSaltoFinale = a.RigheSaltoFinale;
            _p.Taglio = a.Taglio; _p.Cicalino = a.Cicalino; _p.Inversione = a.Inversione; _p.ControlloStato = a.ControlloStato;
            Mostra();
        }

        private async Task GestisciIPAsync()
        {
            Leggi();
            var nuovo = await GestioneIPBox.Modifica(_elencoIP());
            if (nuovo == null) return;
            await _salvaIP(nuovo);
            Mostra();
        }

        private async Task ProvaAsync()
        {
            if (_prova) return;
            Leggi();
            if (!_p.Assegnata) { await MessaggioBox.Avviso("PROVA", "Scegli prima la stampante."); return; }
            _prova = true;
            try
            {
                var par = _p.Copia();
                string intestazione = ContestoCassa.Impostazioni.IntestazioneConto1;
                int tipo = ContestoCassa.Impostazioni.Sale.CicalinoComande;
                var (ok, msg) = await Task.Run(() => PosTouch.Core.Conti.ServizioConto.ProvaAsync(par, intestazione, tipo));
                if (ok) await MessaggioBox.Successo("STAMPA DI PROVA", msg);
                else await MessaggioBox.Errore("STAMPA DI PROVA", msg);
            }
            finally { _prova = false; }
        }

        /// <param name="elencoIP">Elenco IP corrente (Gestione porte TCP/IP).</param>
        /// <param name="salvaIP">Salva il nuovo elenco IP quando lo si cambia da qui.</param>
        public static Task<ParametriStampa> Modifica(string nome, ParametriStampa attuali, Func<List<string>> elencoIP, Func<List<string>, Task> salvaIP)
            => Dialogo.Mostra(new ConfiguraStampaBox(nome, attuali, elencoIP, salvaIP));
    }
}
