using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;

namespace PosTouch.App.Dialoghi
{
    /// <summary>
    /// "Stampanti TCP/IP collegate" (Gestione porte TCP/IP di POSBY): elenco degli IP delle stampanti di rete,
    /// tastierino per scriverli, ➕ aggiungi, 🗑 elimina, 🔍 cerca le stampanti nella rete (porta 9100).
    /// Restituisce il nuovo elenco (null = annullato).
    /// </summary>
    public class GestioneIPBox : DialogoBase<List<string>>
    {
        private readonly List<string> _ip;
        private readonly VerticalStackLayout _elenco = new VerticalStackLayout { Spacing = 2 };
        /// <summary>I 4 numeri dell'indirizzo: si tocca quello da cambiare (anche la classe di rete), le cifre lo riscrivono.</summary>
        private readonly string[] _ottetti = { "", "", "", "" };
        private readonly Label[] _caselle = new Label[4];
        private int _attivo;
        private bool _sovrascrivi = true;   // la prima cifra dopo aver scelto una casella la riscrive da capo
        private readonly Button _cerca;
        private string _selezionato;
        private bool _ricerca;

        private GestioneIPBox(IEnumerable<string> attuali)
        {
            _ip = attuali.ToList();
            var ultimo = _ip.LastOrDefault();
            if (ultimo != null) Carica(PosTouch.Core.Configurazione.ConfigStampe.Separa(ultimo).ip, ultimaCasella: true);   // si cambia solo l'ultimo numero

            // ── sinistra: elenco + display ──
            var sinistra = new Grid { RowDefinitions = { new(GridLength.Star), new(new GridLength(70)) }, RowSpacing = 8 };
            sinistra.Add(Ui.Riquadro(new ScrollView { Content = _elenco }, "#F5EEE6", 6, new Thickness(6)), 0, 0);
            var display = new Grid { ColumnSpacing = 4, Padding = new Thickness(6, 4) };
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                display.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                if (i < 3) display.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                _caselle[i] = new Label { FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
                var casella = Ui.Riquadro(_caselle[i], "#FFFFFF", 4, null, "#BDBDBD", 2);
                Ui.Tocco(casella, () => { _attivo = k; _sovrascrivi = true; Aggiorna(); });
                display.Add(casella, i * 2, 0);
                if (i < 3) display.Add(new Label { Text = ".", FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black, VerticalTextAlignment = TextAlignment.End }, i * 2 + 1, 0);
            }
            sinistra.Add(Ui.Riquadro(display, "#FFF9C4", 6), 0, 1);

            // ── destra: tastierino ──
            var tasti = Ui.Griglia(4, 4, 4);
            string[] cifre = { "7", "8", "9", "4", "5", "6", "1", "2", "3" };
            for (int i = 0; i < 9; i++) { string c = cifre[i]; tasti.Add(Tasto(c, () => Scrivi(c)), i % 3, i / 3); }
            tasti.Add(Tasto(".", () => Scrivi(".")), 0, 3);
            var zero = Tasto("0", () => Scrivi("0"));
            tasti.Add(zero, 1, 3); Grid.SetColumnSpan(zero, 2);
            var canc = Ui.Tasto("C", "#5D4037", "#FF4081", 26, 4, (s, e) => { _ottetti[_attivo] = ""; _sovrascrivi = false; Aggiorna(); });
            tasti.Add(canc, 3, 0); Grid.SetRowSpan(canc, 4);
            var aggiungi = Ui.Tasto("➕ AGGIUNGI", "#37474F", "#FFFFFF", 16, 6, (s, e) => _ = AggiungiAsync(), 60);

            var destra = new Grid { RowDefinitions = { new(GridLength.Star), new(new GridLength(70)) }, RowSpacing = 8 };
            destra.Add(tasti, 0, 0);
            destra.Add(aggiungi, 0, 1);

            var corpo = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 12, HeightRequest = 380 };
            corpo.Add(sinistra, 0, 0);
            corpo.Add(destra, 1, 0);

            // ── piede ──
            _cerca = Ui.Tasto("🔍 CERCA", "#455A64", fontSize: 15, altezza: 56, clic: (s, e) => _ = CercaAsync());
            var piede = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(new GridLength(2, GridUnitType.Star)) }, ColumnSpacing = 10 };
            piede.Add(Ui.Tasto("⬅ ESCI", "#C2185B", fontSize: 15, altezza: 56, clic: (s, e) => Chiudi(null)), 0, 0);
            piede.Add(_cerca, 1, 0);
            piede.Add(Ui.Tasto("🗑 ELIMINA", "#F9A825", "#263238", 15, 8, (s, e) => _ = EliminaAsync(), 56), 2, 0);
            piede.Add(Ui.Tasto("✅ OK", "#2E7D32", fontSize: 16, altezza: 56, clic: (s, e) => Chiudi(_ip.ToList())), 3, 0);

            var tutto = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(16) };
            tutto.Add(new Label { Text = "Stampanti TCP/IP collegate", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center });
            tutto.Add(corpo);
            tutto.Add(piede);

            var riquadro = new Border
            {
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Ui.C("#263238"), StrokeShape = new RoundRectangle { CornerRadius = 12 }, StrokeThickness = 0,
                Content = new ScrollView { Content = tutto }
            };
            Content = riquadro;
            Dimensiona(riquadro, 860);
            DisegnaElenco();
            Aggiorna();
        }

        private static Button Tasto(string t, Action a) => Ui.Tasto(t, "#424242", "#4DD0E1", 26, 4, (s, e) => a());

        /// <summary>Carica un indirizzo nelle 4 caselle (tocco su un IP dell'elenco, o l'ultimo all'apertura).</summary>
        private void Carica(string ip, bool ultimaCasella)
        {
            var parti = (ip ?? "").Split('.');
            for (int i = 0; i < 4; i++) _ottetti[i] = i < parti.Length ? parti[i] : "";
            _attivo = ultimaCasella ? 3 : 0;
            _sovrascrivi = true;
        }

        private void Scrivi(string c)
        {
            if (c == ".") { Avanza(); return; }
            string attuale = _sovrascrivi ? "" : _ottetti[_attivo];
            _sovrascrivi = false;
            if (attuale.Length >= 3 || int.Parse(attuale + c) > 255) { Avanza(); attuale = ""; _sovrascrivi = false; }
            _ottetti[_attivo] = attuale + c;
            if (_ottetti[_attivo].Length == 3) Avanza();   // 3 cifre: passa al numero successivo
            Aggiorna();
        }

        private void Avanza()
        {
            if (_attivo < 3) { _attivo++; _sovrascrivi = true; }
            Aggiorna();
        }

        private string Scritto => string.Join(".", _ottetti);

        private void Aggiorna()
        {
            if (_caselle[0] == null) return;
            for (int i = 0; i < 4; i++)
            {
                _caselle[i].Text = _ottetti[i].Length == 0 ? "_" : _ottetti[i];
                ((Border)_caselle[i].Parent).Stroke = new SolidColorBrush(Ui.C(i == _attivo ? "#1565C0" : "#BDBDBD"));
                ((Border)_caselle[i].Parent).BackgroundColor = Ui.C(i == _attivo ? "#E3F2FD" : "#FFFFFF");
            }
        }

        private void DisegnaElenco()
        {
            _elenco.Children.Clear();
            if (_ip.Count == 0) _elenco.Add(Ui.Testo("Nessuna stampante.\nScrivi un IP e premi ➕ AGGIUNGI,\noppure 🔍 CERCA.", 15, "#78909C"));
            foreach (var ip in _ip)
            {
                bool sel = ip == _selezionato;
                var l = new Label
                {
                    Text = ip, FontSize = 26, TextColor = Ui.C(sel ? "#FFFFFF" : "#000000"), Padding = new Thickness(8, 4),
                    BackgroundColor = Ui.C(sel ? "#1565C0" : "#00000000")
                };
                Ui.Tocco(l, () =>
                {
                    _selezionato = sel ? null : ip;
                    if (_selezionato != null) Carica(PosTouch.Core.Configurazione.ConfigStampe.Separa(ip).ip, ultimaCasella: true);
                    DisegnaElenco(); Aggiorna();
                });
                _elenco.Add(l);
            }
        }

        private async Task AggiungiAsync()
        {
            string ip = Scritto;
            if (_ottetti.Any(o => o.Length == 0) || !IPAddress.TryParse(ip, out var a) || a.AddressFamily != AddressFamily.InterNetwork)
            { await MessaggioBox.Avviso("IP NON VALIDO", "Completa i 4 numeri, es. 192.168.1.52.\nTocca una casella per cambiarla."); return; }
            ip = string.Join(".", _ottetti.Select(o => int.Parse(o).ToString()));   // "010" → "10"
            if (_ip.Contains(ip)) { await MessaggioBox.Avviso("IP GIÀ PRESENTE", $"{ip} è già nell'elenco."); return; }
            _ip.Add(ip);
            _ottetti[3] = ""; _attivo = 3; _sovrascrivi = true;   // pronto per la prossima stampante della stessa rete
            DisegnaElenco(); Aggiorna();
        }

        private async Task EliminaAsync()
        {
            if (_selezionato == null) { await MessaggioBox.Avviso("ELIMINA", "Tocca prima l'indirizzo da eliminare."); return; }
            if (!await MessaggioBox.Conferma("ELIMINA STAMPANTE", $"Togliere {_selezionato} dall'elenco?\n\nLe stampe che la usano resteranno senza stampante.")) return;
            _ip.Remove(_selezionato);
            _selezionato = null;
            DisegnaElenco();
        }

        /// <summary>🔍 Cerca nella rete del tablet (o in quella dell'IP scritto) le stampanti con la porta 9100 aperta.</summary>
        private async Task CercaAsync()
        {
            if (_ricerca) return;
            string rete = ReteDaCercare();
            if (rete == null) { await MessaggioBox.Avviso("CERCA", "Scrivi i primi tre numeri della rete (es. 10.10.10.) oppure collega il tablet alla rete."); return; }
            _ricerca = true;
            _cerca.Text = "⏳ ...";
            try
            {
                var trovate = await Task.Run(async () =>
                {
                    var compiti = Enumerable.Range(1, 254).Select(async n =>
                    {
                        string ip = rete + n;
                        try
                        {
                            using var c = new TcpClient();
                            var t = c.ConnectAsync(ip, 9100);
                            return await Task.WhenAny(t, Task.Delay(600)) == t && c.Connected ? ip : null;
                        }
                        catch { return null; }
                    });
                    return (await Task.WhenAll(compiti)).Where(x => x != null).ToList();
                });
                var nuove = trovate.Where(ip => !_ip.Contains(ip)).ToList();
                _ip.AddRange(nuove);
                DisegnaElenco();
                await MessaggioBox.Info("🔍 RICERCA STAMPANTI", trovate.Count == 0
                    ? $"Nessuna stampante trovata in {rete}1-254 (porta 9100)."
                    : $"Trovate {trovate.Count} stampanti in {rete}x:\n{string.Join("\n", trovate)}\n\n{(nuove.Count > 0 ? $"Aggiunte {nuove.Count} nuove." : "Erano già tutte nell'elenco.")}");
            }
            finally { _ricerca = false; _cerca.Text = "🔍 CERCA"; }
        }

        private string ReteDaCercare()
        {
            if (_ottetti.Take(3).All(p => int.TryParse(p, out var v) && v <= 255)) return string.Join(".", _ottetti.Take(3).Select(o => int.Parse(o))) + ".";
            try
            {
                foreach (var a in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                    { var b = a.GetAddressBytes(); return $"{b[0]}.{b[1]}.{b[2]}."; }
            }
            catch { }
            return null;
        }

        public static Task<List<string>> Modifica(IEnumerable<string> attuali) => Dialogo.Mostra(new GestioneIPBox(attuali));
    }
}
