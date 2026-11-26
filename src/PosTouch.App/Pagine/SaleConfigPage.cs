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
    public class SaleConfigPage : PaginaPos
    {
        private readonly ConfigSale _c = ContestoCassa.Impostazioni.Sale.Copia();
        private readonly ContentView _contenuto = new ContentView();
        private readonly HorizontalStackLayout _schede = new HorizontalStackLayout { Spacing = 2 };
        private int _scheda;
        private static readonly string[] NomiSchede = { "🍽️ SALE", "🖨️ STAMPANTI", "👨‍🍳 CENTRI", "🔀 STAMPANTI PER SALA", "⚙️ OPZIONI" };
        private static readonly string[] Stili = { "1 Normale", "2 Grassetto", "3 Doppia altezza", "4 Doppia altezza + grassetto", "5 Doppia larghezza",
            "6 Doppia larghezza + grassetto", "7 Doppia (2×2)", "8 Doppia (2×2) + grassetto", "9 Tripla (3×3)", "10 Tripla + grassetto", "11 Tripla altezza", "12 Tripla larghezza" };
        private static readonly string[] Cicalini = { "Spento", "BEL (3 bip)", "Epson ESC(A", "ESC B", "DLE DC4" };

        public SaleConfigPage()
        {
            BackgroundColor = Ui.C("#F5F5F5");
            var titolo = new Label { Text = "🍽️ SALE E COMANDE", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
            var chiudi = Ui.Tasto("✖ CHIUDI", "#78909C", fontSize: 15, altezza: 46, clic: async (s, e) => await Navigazione.Chiudi(this));
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

        protected override bool Indietro() { _ = Navigazione.Chiudi(this); return true; }

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
            _contenuto.Content = n switch { 0 => SchedaSale(), 1 => SchedaStampanti(), 2 => SchedaCentri(), 3 => SchedaPerSala(), _ => SchedaOpzioni() };
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

        // ══════════════ SALE ══════════════

        private View SchedaSale()
        {
            var v = new VerticalStackLayout { Spacing = 10 };
            v.Add(Card(new VerticalStackLayout
            {
                Children =
                {
                    Spunta("Abilita sale e tavoli (tasto 🍽️ SALE in vendita)", _c.AbilitaSale, x => _c.AbilitaSale = x),
                    Spunta("Abilita conti clienti sospesi (scheda 👤 CONTI, con il nome del cliente)", _c.AbilitaContiClienti, x => _c.AbilitaContiClienti = x),
                    new HorizontalStackLayout
                    {
                        Spacing = 10, Margin = new Thickness(34, 0, 0, 6),
                        Children =
                        {
                            new Label { Text = "Numero conti clienti:", FontSize = 15, VerticalOptions = LayoutOptions.Center },
                            Campo(_c.NumeroSlotContiClienti.ToString(), t => _c.NumeroSlotContiClienti = int.TryParse(t, out var n) ? n : -1, Keyboard.Numeric, 90)
                        }
                    },
                    Ui.Testo("Il coperto (se scelto) viene chiesto all'apertura di un tavolo nuovo.", 13, "#999999")
                }
            }));
            var prodotti = ContestoCassa.Catalogo.TuttiProdotti.ToList();
            var vociCoperto = new[] { "Nessuno" }.Concat(prodotti.Select(p => $"{p.Descrizione} ({p.CategoriaNome})")).ToList();

            var griglia = new Grid { ColumnSpacing = 8, RowSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(new GridLength(110)), new(new GridLength(120)), new(GridLength.Star), new(GridLength.Auto) } };
            griglia.Add(Intestazione("NOME SALA"), 0, 0); griglia.Add(Intestazione("N. TAVOLI"), 1, 0);
            griglia.Add(Intestazione("PRIMO N."), 2, 0); griglia.Add(Intestazione("COPERTO"), 3, 0);
            int riga = 1;
            foreach (var sala in _c.Sale.ToList())
            {
                griglia.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                griglia.Add(Campo(sala.Nome, t => sala.Nome = t.Trim()), 0, riga);
                griglia.Add(Campo(sala.NumeroTavoli.ToString(), t => sala.NumeroTavoli = int.TryParse(t, out var n) ? n : -1, Keyboard.Numeric), 1, riga);
                griglia.Add(Campo(sala.NumeroIniziale.ToString(), t => sala.NumeroIniziale = int.TryParse(t, out var n) ? n : -1, Keyboard.Numeric), 2, riga);
                int ic = prodotti.FindIndex(p => p.Id == sala.CopertoProdottoId);
                griglia.Add(InBox(Scelta("Articolo coperto", vociCoperto, ic >= 0 ? ic + 1 : 0, i => sala.CopertoProdottoId = i > 0 ? prodotti[i - 1].Id : 0, 3)), 3, riga);
                griglia.Add(TastoElimina(async () =>
                {
                    if (ContestoCassa.Tavoli.Aperti().Any(t => t.SalaId == sala.Id && t.Articoli > 0))
                    { await MessaggioBox.Avviso("SALA CON TAVOLI APERTI", "Chiudi prima i tavoli aperti di questa sala."); return; }
                    if (!await MessaggioBox.Conferma("ELIMINA SALA", $"Eliminare la sala {sala.Nome}?")) return;
                    _c.Sale.Remove(sala);
                    _c.StampantiSala.RemoveAll(x => x.SalaId == sala.Id);
                    Mostra(_scheda);
                }), 4, riga);
                riga++;
            }
            griglia.RowDefinitions.Insert(0, new RowDefinition(GridLength.Auto));
            v.Add(Card(griglia));
            v.Add(Ui.Tasto("➕ AGGIUNGI SALA", "#00897B", altezza: 52, clic: (s, e) =>
            {
                int id = _c.Sale.Count == 0 ? 1 : _c.Sale.Max(x => x.Id) + 1;
                int primo = _c.Sale.Count == 0 ? 1 : _c.Sale.Max(x => x.NumeroIniziale + x.NumeroTavoli);
                _c.Sale.Add(new Sala { Id = id, Nome = $"Sala {id}", NumeroTavoli = 10, NumeroIniziale = primo });
                Mostra(_scheda);
            }));
            return v;
        }

        // ══════════════ STAMPANTI ══════════════

        private View SchedaStampanti()
        {
            var v = new VerticalStackLayout { Spacing = 10 };
            v.Add(Card(Ui.Testo("Stampanti ESC/POS in rete (porta 9100) usate per comande, storni e VAI.\nPoi assegnale ai centri di produzione.", 14, "#607D8B")));
            var griglia = new Grid { ColumnSpacing = 8, RowSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(new GridLength(200)), new(new GridLength(100)), new(GridLength.Auto), new(GridLength.Auto) } };
            griglia.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            griglia.Add(Intestazione("NOME"), 0, 0); griglia.Add(Intestazione("INDIRIZZO IP"), 1, 0); griglia.Add(Intestazione("PORTA"), 2, 0);
            int riga = 1;
            foreach (var st in _c.Stampanti.ToList())
            {
                griglia.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                griglia.Add(Campo(st.Nome, t => st.Nome = t.Trim(), segnaposto: "es. Cucina"), 0, riga);
                griglia.Add(Campo(st.IP, t => st.IP = t.Trim(), Keyboard.Url, segnaposto: "192.168.1.50"), 1, riga);
                griglia.Add(Campo(st.Porta.ToString(), t => st.Porta = int.TryParse(t, out var n) ? n : -1, Keyboard.Numeric), 2, riga);
                var prova = Ui.Tasto("🖨️ PROVA", "#9C27B0", fontSize: 14, altezza: 46);
                prova.Padding = new Thickness(12, 0);
                prova.Clicked += async (s, e) =>
                {
                    if (!IPAddress.TryParse(st.IP, out _) || st.Porta < 1 || st.Porta > 65535) { await MessaggioBox.Avviso("STAMPANTE", "IP o porta non validi."); return; }
                    prova.IsEnabled = false;
                    string ip = st.IP, nome = st.Nome; int porta = st.Porta;
                    var (ok, msg) = await Task.Run(() => PosTouch.Core.Conti.ServizioConto.ProvaAsync(ip, porta, nome));
                    prova.IsEnabled = true;
                    if (ok) await MessaggioBox.Successo("STAMPANTE", msg); else await MessaggioBox.Errore("STAMPANTE", msg);
                };
                griglia.Add(prova, 3, riga);
                griglia.Add(TastoElimina(async () =>
                {
                    if (!await MessaggioBox.Conferma("ELIMINA STAMPANTE", $"Eliminare la stampante {st.Nome}?\nI centri e le sale che la usano resteranno senza stampante.")) return;
                    _c.Stampanti.Remove(st);
                    foreach (var c in _c.Centri.Where(x => x.StampanteId == st.Id)) c.StampanteId = 0;
                    _c.StampantiSala.RemoveAll(x => x.StampanteId == st.Id);
                    Mostra(_scheda);
                }), 4, riga);
                riga++;
            }
            v.Add(Card(griglia));
            v.Add(Ui.Tasto("➕ AGGIUNGI STAMPANTE", "#00897B", altezza: 52, clic: (s, e) =>
            {
                int id = _c.Stampanti.Count == 0 ? 1 : _c.Stampanti.Max(x => x.Id) + 1;
                _c.Stampanti.Add(new StampanteRete { Id = id, Nome = $"Stampante {id}", IP = "192.168.1.", Porta = 9100 });
                Mostra(_scheda);
            }));
            return v;
        }

        // ══════════════ CENTRI ══════════════

        private List<string> VociStampanti(string prima) => new[] { prima }.Concat(_c.Stampanti.Select(s => $"{s.Nome} ({s.IP})")).ToList();

        private View SchedaCentri()
        {
            var v = new VerticalStackLayout { Spacing = 10 };
            v.Add(Card(Ui.Testo("Il centro si assegna alla CATEGORIA (Back office); il singolo articolo può avere un centro diverso.\n" +
                                "Gli articoli senza centro non vengono stampati in comanda.", 14, "#607D8B")));
            var griglia = new Grid { ColumnSpacing = 8, RowSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Auto) } };
            griglia.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            griglia.Add(Intestazione("CENTRO DI PRODUZIONE"), 0, 0); griglia.Add(Intestazione("STAMPANTE PREDEFINITA"), 1, 0);
            var voci = VociStampanti("Nessuna (non stampa)");
            int riga = 1;
            foreach (var c in _c.Centri.ToList())
            {
                griglia.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                griglia.Add(Campo(c.Nome, t => c.Nome = t.Trim().ToUpperInvariant(), segnaposto: "es. CUCINA"), 0, riga);
                int i = _c.Stampanti.FindIndex(s => s.Id == c.StampanteId);
                griglia.Add(InBox(Scelta("Stampante del centro", voci, i >= 0 ? i + 1 : 0, k => c.StampanteId = k > 0 ? _c.Stampanti[k - 1].Id : 0)), 1, riga);
                griglia.Add(TastoElimina(async () =>
                {
                    int usi = ContestoCassa.Catalogo.TutteCategorie.Count(x => x.CentroProduzioneId == c.Id) +
                              ContestoCassa.Catalogo.TuttiProdotti.Count(x => x.CentroProduzioneId == c.Id);
                    if (!await MessaggioBox.Conferma("ELIMINA CENTRO", $"Eliminare il centro {c.Nome}?" +
                        (usi > 0 ? $"\n\n⚠️ È usato da {usi} categorie/articoli: non stamperanno più in comanda." : ""))) return;
                    _c.Centri.Remove(c);
                    _c.StampantiSala.RemoveAll(x => x.CentroId == c.Id);
                    Mostra(_scheda);
                }), 2, riga);
                riga++;
            }
            v.Add(Card(griglia));
            v.Add(Ui.Tasto("➕ AGGIUNGI CENTRO", "#00897B", altezza: 52, clic: (s, e) =>
            {
                int id = _c.Centri.Count == 0 ? 1 : _c.Centri.Max(x => x.Id) + 1;
                _c.Centri.Add(new CentroProduzione { Id = id, Nome = $"CENTRO {id}" });
                Mostra(_scheda);
            }));
            return v;
        }

        // ══════════════ STAMPANTI PER SALA (come POSBY) ══════════════

        private View SchedaPerSala()
        {
            var v = new VerticalStackLayout { Spacing = 10 };
            v.Add(Card(Ui.Testo("Facoltativo: in una sala un centro può stampare su un'altra stampante\n" +
                                "(es. in Terrazza il BAR esce sulla stampante del bar esterno). \"Predefinita\" = stampante del centro.", 14, "#607D8B")));
            if (_c.Sale.Count == 0 || _c.Centri.Count == 0)
            {
                v.Add(Card(Ui.Testo("Configura prima almeno una sala e un centro.", 15, "#C62828")));
                return v;
            }
            var griglia = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            griglia.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(160)));
            foreach (var _ in _c.Centri) griglia.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            griglia.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int k = 0; k < _c.Centri.Count; k++) griglia.Add(Intestazione(_c.Centri[k].Nome), k + 1, 0);
            int riga = 1;
            foreach (var sala in _c.Sale)
            {
                griglia.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                griglia.Add(new Label { Text = sala.Nome, FontSize = 16, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center }, 0, riga);
                for (int k = 0; k < _c.Centri.Count; k++)
                {
                    var centro = _c.Centri[k];
                    string predef = _c.StampanteDa(centro.StampanteId)?.Nome;
                    var voci = VociStampanti(predef == null ? "Predefinita (nessuna)" : $"Predefinita ({predef})");
                    var ecc = _c.StampantiSala.FirstOrDefault(x => x.SalaId == sala.Id && x.CentroId == centro.Id);
                    int i = ecc == null ? -1 : _c.Stampanti.FindIndex(s => s.Id == ecc.StampanteId);
                    int salaId = sala.Id, centroId = centro.Id;
                    griglia.Add(InBox(Scelta($"{sala.Nome} — {centro.Nome}", voci, i >= 0 ? i + 1 : 0, sel =>
                    {
                        _c.StampantiSala.RemoveAll(x => x.SalaId == salaId && x.CentroId == centroId);
                        if (sel > 0) _c.StampantiSala.Add(new StampanteSala { SalaId = salaId, CentroId = centroId, StampanteId = _c.Stampanti[sel - 1].Id });
                    })), k + 1, riga);
                }
                riga++;
            }
            v.Add(Card(griglia));
            return v;
        }

        // ══════════════ OPZIONI ══════════════

        private View SchedaOpzioni()
        {
            var v = new VerticalStackLayout { Spacing = 10 };
            v.Add(Card(new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    Spunta("Abilita stampa comande", _c.AbilitaComande, x => _c.AbilitaComande = x),
                    Spunta("Stampa riepilogo altri centri in fondo alla comanda", _c.StampaRiepilogoAltriCentri, x => _c.StampaRiepilogoAltriCentri = x),
                    Spunta("Accorpa righe uguali in comanda", _c.AccorpaStampaComanda, x => _c.AccorpaStampaComanda = x),
                    Spunta("Stampa gli storni anche con 🚫 NO STAMPA", _c.StampaStorniConNoStampa, x => _c.StampaStorniConNoStampa = x),
                }
            }));
            var stili = new Grid { ColumnSpacing = 10, RowSpacing = 8, ColumnDefinitions = { new(new GridLength(240)), new(GridLength.Star) } };
            for (int i = 0; i < 4; i++) stili.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            stili.Add(new Label { Text = "Stile articoli:", FontSize = 16, VerticalOptions = LayoutOptions.Center }, 0, 0);
            stili.Add(InBox(Scelta("Stile articoli in comanda", Stili, Math.Clamp(_c.StileComandaProdotto - 1, 0, 11), i => _c.StileComandaProdotto = i + 1)), 1, 0);
            stili.Add(new Label { Text = "Stile separatori portata:", FontSize = 16, VerticalOptions = LayoutOptions.Center }, 0, 1);
            stili.Add(InBox(Scelta("Stile separatori", Stili, Math.Clamp(_c.StileComandaSeparatore - 1, 0, 11), i => _c.StileComandaSeparatore = i + 1)), 1, 1);
            stili.Add(new Label { Text = "Cicalino comande:", FontSize = 16, VerticalOptions = LayoutOptions.Center }, 0, 2);
            stili.Add(InBox(Scelta("Cicalino", Cicalini, Math.Clamp(_c.CicalinoComande, 0, 4), i => _c.CicalinoComande = i)), 1, 2);
            v.Add(Card(stili));
            v.Add(Card(new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    Spunta("Abilita portate (📑 FUNZIONI e ⏩ AVANZA al tavolo)", _c.AbilitaPortate, x => _c.AbilitaPortate = x),
                    Spunta("Abilita 🔀 SPOSTA tavolo", _c.AbilitaSpostaTavolo, x => _c.AbilitaSpostaTavolo = x),
                    Spunta("Abilita 🔗 UNISCI conti", _c.AbilitaUnisciConti, x => _c.AbilitaUnisciConti = x),
                    new Label { Text = "Separatori portate, in ordine (separati da virgola):", FontSize = 15 },
                    Campo(string.Join(", ", _c.SeparatoriPortate), t => _c.SeparatoriPortate = t.Split(',').Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().ToList(),
                          segnaposto: "SEGUE, SEGUE 2, SEGUE 3")
                }
            }));
            return v;
        }

        // ══════════════ SALVA ══════════════

        private async Task SalvaAsync()
        {
            string errore = null;
            foreach (var s in _c.Sale)
                if (string.IsNullOrWhiteSpace(s.Nome) || s.NumeroTavoli < 1 || s.NumeroTavoli > 500 || s.NumeroIniziale < 0)
                { errore = $"Sala \"{s.Nome}\": nome obbligatorio, tavoli da 1 a 500, primo numero valido."; break; }
            if (errore == null && _c.Sale.GroupBy(s => s.Nome.ToUpperInvariant()).Any(g => g.Count() > 1)) errore = "Due sale hanno lo stesso nome.";
            if (errore == null && (_c.NumeroSlotContiClienti < 1 || _c.NumeroSlotContiClienti > 200)) errore = "Numero conti clienti: da 1 a 200.";
            if (errore == null)
                foreach (var st in _c.Stampanti)
                    if (string.IsNullOrWhiteSpace(st.Nome) || !IPAddress.TryParse(st.IP, out _) || st.Porta < 1 || st.Porta > 65535)
                    { errore = $"Stampante \"{st.Nome}\": nome, IP e porta devono essere validi."; break; }
            if (errore == null && _c.Centri.Any(c => string.IsNullOrWhiteSpace(c.Nome))) errore = "Ogni centro deve avere un nome.";
            if (errore == null && _c.AbilitaPortate && _c.SeparatoriPortate.Count == 0) errore = "Con le portate abilitate serve almeno un separatore.";
            if (errore != null) { await MessaggioBox.Avviso("VALORE NON VALIDO", errore); return; }

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
