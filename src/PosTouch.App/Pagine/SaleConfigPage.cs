using System.Net;
using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// 🍽️ SALE E COMANDE: sale e tavoli, stampanti, centri di produzione, stampanti per sala (come POSBY)
    /// e opzioni comande/portate. Si lavora su una copia: si salva solo con 💾 SALVA.
    /// </summary>
    public partial class SaleConfigPage : PaginaPos
    {
        private readonly ConfigSale _c = ContestoCassa.Impostazioni.Sale.Copia();
        private readonly ContentView _contenuto = new ContentView();
        private readonly HorizontalStackLayout _schede = new HorizontalStackLayout { Spacing = 2 };
        private int _scheda;
        private static readonly string[] NomiSchede = { "🍽️ SALE", "⚙️ OPZIONI COMANDE" };
        private static readonly string[] Stili = { "1 Normale", "2 Grassetto", "3 Doppia altezza", "4 Doppia altezza + grassetto", "5 Doppia larghezza",
            "6 Doppia larghezza + grassetto", "7 Doppia (2×2)", "8 Doppia (2×2) + grassetto", "9 Tripla (3×3)", "10 Tripla + grassetto", "11 Tripla altezza", "12 Tripla larghezza" };
        private static readonly string[] Cicalini = { "Spento", "BEL (3 bip)", "Epson ESC(A", "ESC B", "DLE DC4" };

        public SaleConfigPage()
        {
            BackgroundColor = Ui.C("#F5F5F5");
            var titolo = new Label { Text = "🍽️ SALE E TAVOLI", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
            _firma = _c.Firma();
            var chiudi = Ui.Tasto("✖ CHIUDI", "#78909C", fontSize: 15, altezza: 46, clic: async (s, e) => await ChiudiAsync());
            var salva = Ui.Tasto("💾 SALVA", "#43A047", fontSize: 15, altezza: 46, clic: async (s, e) => await SalvaAsync());
            chiudi.WidthRequest = 130; salva.WidthRequest = 130;
            var testa = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 10, BackgroundColor = Ui.C("#00897B"), Padding = new Thickness(16, 8) };
            testa.Add(titolo, 0, 0); testa.Add(chiudi, 1, 0); testa.Add(salva, 2, 0);

            var radice = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) } };
            radice.Add(testa, 0, 0);
            radice.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _schede, Margin = new Thickness(10, 6, 10, 0) }, 0, 1);
            radice.Add(new ScrollView { Content = _contenuto, Padding = new Thickness(14) }, 0, 2);
            Content = radice;
            Mostra(0);
        }

        protected override bool Indietro() { _ = ChiudiAsync(); return true; }

        private readonly string _firma;
        private bool _chiusura;

        /// <summary>Come Stampe: con modifiche non salvate si chiede prima di chiudere.</summary>
        private async Task ChiudiAsync()
        {
            if (_chiusura) return;
            _chiusura = true;
            try
            {
                if (_c.Firma() != _firma)
                {
                    var scelta = await MessaggioBox.SalvaPrimaDiChiudere("sale e comande");
                    if (scelta == null) return;                       // resta nella pagina
                    if (scelta == true) { await SalvaAsync(); return; }
                }
                await Navigazione.Chiudi(this);
            }
            finally { _chiusura = false; }
        }

        private void Mostra(int n)
        {
            _scheda = n;
            _schede.Children.Clear();
            for (int i = 0; i < NomiSchede.Length; i++)
            {
                int k = i;
                var b = Ui.Tasto(NomiSchede[i], i == n ? "#00897B" : "#CFD8DC", i == n ? "#FFFFFF" : "#37474F", 14, 6, (s, e) => Mostra(k), 44);
                b.Padding = new Thickness(16, 0);
                _schede.Add(b);
            }
            _contenuto.Content = n == 1 ? SchedaOpzioni() : SchedaSale();
        }

        // ══════════════ helper UI ══════════════

        private static Border Card(View v) => Ui.Riquadro(v, "#FFFFFF", 10, new Thickness(16), "#E0E0E0", 1);

        private static Entry Campo(string testo, Action<string> cambiato, Keyboard tastiera = null, double larghezza = -1, string segnaposto = "")
        {
            var e = new Entry { Text = testo, FontSize = 16, Keyboard = tastiera ?? Keyboard.Default, Placeholder = segnaposto, BackgroundColor = Ui.C("#FAFAFA") };
            if (larghezza > 0) e.WidthRequest = larghezza;
            e.TextChanged += (s, a) => cambiato(a.NewTextValue ?? "");
            return e;
        }

        private static Label Intestazione(string t) => new Label { Text = t, FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#78909C") };

        private static View Spunta(string testo, bool valore, Action<bool> cambiato)
        {
            var cb = new CheckBox { IsChecked = valore, Color = Ui.C("#4CAF50") };
            cb.CheckedChanged += (s, e) => cambiato(e.Value);
            var l = new Label { Text = testo, FontSize = 16, VerticalOptions = LayoutOptions.Center };
            Ui.Tocco(l, () => cb.IsChecked = !cb.IsChecked);
            return new HorizontalStackLayout { Children = { cb, l } };
        }

        private static CampoScelta Scelta(string titolo, IList<string> voci, int indice, Action<int> cambiato, int colonne = 2)
        {
            var c = new CampoScelta { Titolo = titolo, Colonne = colonne, FontSize = 15, Voci = voci, Indice = indice };
            c.Cambiato += (s, e) => cambiato(c.Indice);
            return c;
        }

        private static View InBox(View v) => Ui.Riquadro(v, "#FAFAFA", 6, new Thickness(10, 10), "#E0E0E0", 1);

        private Button TastoElimina(Func<Task> azione)
        {
            var b = Ui.Tasto("🗑️", "#F44336", fontSize: 18, altezza: 46, clic: async (s, e) => await azione());
            b.WidthRequest = 54;
            return b;
        }

        // ══════════════ SALVA ══════════════

        /// <summary>Tavoli aperti (con articoli) che non rientrerebbero più in nessuna sala/slot della nuova configurazione.</summary>
        private List<string> TavoliFuoriConfigurazione()
        {
            var r = new List<string>();
            foreach (var t in ContestoCassa.Tavoli.Aperti().Where(t => t.Occupato))
            {
                bool visibile;
                if (t.SalaId == ConfigSale.SalaConti)
                    visibile = _c.AbilitaContiClienti && t.Numero >= 1 && t.Numero <= _c.NumeroSlotContiClienti;
                else
                {
                    var sala = _c.Sale.FirstOrDefault(s => s.Id == t.SalaId);
                    visibile = _c.AbilitaSale && sala != null && t.Numero >= sala.NumeroIniziale && t.Numero < sala.NumeroIniziale + sala.NumeroTavoli;
                }
                if (!visibile) r.Add(t.SalaId == ConfigSale.SalaConti ? $"Conto cliente {t.Numero}" : $"Sala {t.SalaId} - tavolo {t.Numero}");
            }
            return r;
        }

        private async Task SalvaAsync()
        {
            string errore = null;
            foreach (var s in _c.Sale)
                if (string.IsNullOrWhiteSpace(s.Nome) || s.NumeroTavoli < 1 || s.NumeroTavoli > 500 || s.NumeroIniziale < 0)
                { errore = $"Sala \"{s.Nome}\": nome obbligatorio, tavoli da 1 a 500, primo numero valido."; break; }
            if (errore == null && _c.Sale.GroupBy(s => s.Nome.ToUpperInvariant()).Any(g => g.Count() > 1)) errore = "Due sale hanno lo stesso nome.";
            if (errore == null && (_c.NumeroSlotContiClienti < 1 || _c.NumeroSlotContiClienti > 200)) errore = "Numero conti clienti: da 1 a 200.";
            if (errore == null && _c.AbilitaPortate && _c.SeparatoriPortate.Count == 0) errore = "Con le portate abilitate serve almeno un separatore.";
            if (errore != null) { await MessaggioBox.Avviso("VALORE NON VALIDO", errore); return; }

            // un tavolo aperto non deve sparire dalla mappa con la nuova configurazione
            var fuori = TavoliFuoriConfigurazione();
            if (fuori.Count > 0)
            {
                await MessaggioBox.Avviso("TAVOLI APERTI", "Questi tavoli aperti non sarebbero più visibili con le nuove impostazioni:\n\n"
                    + string.Join("\n", fuori.Take(10)) + (fuori.Count > 10 ? "\n…" : "")
                    + "\n\nIncassali o spostali prima di salvare.");
                return;
            }

            var n = ContestoCassa.Impostazioni.Copia();
            n.Sale = _c.Copia();
            try
            {
                ContestoCassa.SalvaImpostazioni(n);
                await MessaggioBox.Successo("SALE E COMANDE", "Impostazioni salvate.");
                await Navigazione.Chiudi(this);
            }
            catch (Exception ex) { await MessaggioBox.Errore("ERRORE", ex.Message); }
        }
    }
}
