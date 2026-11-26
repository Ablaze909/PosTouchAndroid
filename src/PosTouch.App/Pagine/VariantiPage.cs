using System.Globalization;
using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// 🧩 VARIANTI (Back office): nome, gruppo, tipo (standard ±/preparazione), prezzo aggiunta e rimozione,
    /// categorie in cui compaiono. Stesse tabelle del pos.db del PC (Varianti, CategorieVarianti).
    /// Ogni riga si salva con il suo 💾.
    /// </summary>
    public class VariantiPage : PaginaPos
    {
        private readonly VerticalStackLayout _lista = new VerticalStackLayout { Spacing = 8 };
        private static readonly List<string> Tipi = new() { "Standard (+/-)", "Preparazione" };

        public VariantiPage()
        {
            BackgroundColor = Ui.C("#F5F5F5");
            var testa = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 10, BackgroundColor = Ui.C("#E65100"), Padding = new Thickness(16, 8) };
            testa.Add(new Label { Text = "🧩 VARIANTI", FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center }, 0, 0);
            var nuova = Ui.Tasto("➕ NUOVA", "#43A047", fontSize: 15, altezza: 46, clic: (s, e) => { if (_vuota != null) { _lista.Remove(_vuota); _vuota = null; } _lista.Insert(0, Riga(new Variante { Ordine = 0 })); });
            var chiudi = Ui.Tasto("CHIUDI", "#BF360C", fontSize: 15, altezza: 46, clic: async (s, e) => await Navigazione.Chiudi(this));
            nuova.WidthRequest = 130; chiudi.WidthRequest = 120;
            testa.Add(nuova, 1, 0); testa.Add(chiudi, 2, 0);

            var intest = new Grid { Padding = new Thickness(26, 6, 26, 0), ColumnSpacing = 8 };
            Colonne(intest);
            string[] titoli = { "NOME", "GRUPPO", "TIPO", "+ €", "− €", "CATEGORIE", "ATTIVA" };
            for (int i = 0; i < titoli.Length; i++)
                intest.Add(new Label { Text = titoli[i], FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Ui.C("#78909C") }, i, 0);

            var radice = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) } };
            radice.Add(testa, 0, 0);
            radice.Add(intest, 0, 1);
            radice.Add(new ScrollView { Content = _lista, Padding = new Thickness(14) }, 0, 2);
            Content = radice;
            Carica();
        }

        protected override bool Indietro() { _ = Navigazione.Chiudi(this); return true; }

        private static void Colonne(Grid g)
        {
            foreach (var w in new[] { 2.0, 1.4, 1.4, 0.8, 0.8, 1.4 }) g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(w, GridUnitType.Star)));
            for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        private View _vuota;

        private void Carica()
        {
            _vuota = null;
            _lista.Children.Clear();
            var tutte = ContestoCassa.Catalogo.TutteVarianti;
            if (tutte.Count == 0)
                _lista.Add(_vuota = Ui.Testo("Nessuna variante. Premi ➕ NUOVA per crearne una, poi scegli le categorie in cui compare.", 15, "#78909C"));
            foreach (var v in tutte) _lista.Add(Riga(Copia(v)));
        }

        private static Variante Copia(Variante v) => new Variante
        {
            Id = v.Id, Nome = v.Nome, Gruppo = v.Gruppo, Tipo = v.Tipo, PrezzoAggiuntaCent = v.PrezzoAggiuntaCent,
            PrezzoRimozioneCent = v.PrezzoRimozioneCent, Ordine = v.Ordine, Attiva = v.Attiva, Categorie = v.Categorie.ToList()
        };

        private static string Euro(int cent) => (cent / 100m).ToString("0.00", new CultureInfo("it-IT"));

        private static int? Cent(string t)
        {
            t = (t ?? "").Trim().Replace("€", "").Replace(',', '.');
            if (t.Length == 0) return 0;
            return decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) && v >= 0 && v < 1000
                ? (int)Math.Round(v * 100, MidpointRounding.AwayFromZero) : null;
        }

        private View Riga(Variante v)
        {
            var g = new Grid { ColumnSpacing = 8 };
            Colonne(g);
            var nome = new Entry { Text = v.Nome, FontSize = 15, Placeholder = "es. MOZZARELLA" };
            var gruppo = new Entry { Text = v.Gruppo, FontSize = 15, Placeholder = "Generali" };
            var tipo = new CampoScelta { Titolo = "Tipo variante", Colonne = 2, FontSize = 14, Voci = Tipi, Indice = (int)v.Tipo };
            var piu = new Entry { Text = Euro(v.PrezzoAggiuntaCent), FontSize = 15, Keyboard = Keyboard.Numeric };
            var meno = new Entry { Text = Euro(v.PrezzoRimozioneCent), FontSize = 15, Keyboard = Keyboard.Numeric };
            var categorie = Ui.Tasto("", "#ECEFF1", "#37474F", 13, 6, altezza: 44);
            void TestoCategorie() => categorie.Text = v.Categorie.Count == 0 ? "nessuna" : $"{v.Categorie.Count} categorie";
            TestoCategorie();
            categorie.Clicked += async (s, e) =>
            {
                var tutte = ContestoCassa.Catalogo.TutteCategorie.ToList();
                var scelte = await SceltaMultiplaBox.Chiedi($"Categorie in cui compare {v.Nome}", tutte.Select(c => c.Nome).ToList(),
                    v.Categorie.Select(id => tutte.FindIndex(c => c.Id == id)).Where(i => i >= 0));
                if (scelte == null) return;
                v.Categorie = scelte.Select(i => tutte[i].Id).ToList();
                TestoCategorie();
            };
            var attiva = new Switch { IsToggled = v.Attiva, OnColor = Ui.C("#4CAF50"), VerticalOptions = LayoutOptions.Center };
            var salva = Ui.Tasto("💾", "#43A047", fontSize: 18, altezza: 44);
            salva.WidthRequest = 52;
            salva.Clicked += async (s, e) =>
            {
                var pa = Cent(piu.Text); var pr = Cent(meno.Text);
                if (string.IsNullOrWhiteSpace(nome.Text) || pa == null || pr == null)
                { await MessaggioBox.Avviso("VARIANTE", "Nome obbligatorio; prezzi da 0 a 999,99."); return; }
                v.Nome = nome.Text; v.Gruppo = gruppo.Text ?? ""; v.Tipo = (TipoVariante)Math.Max(0, tipo.Indice);
                v.PrezzoAggiuntaCent = pa.Value; v.PrezzoRimozioneCent = pr.Value; v.Attiva = attiva.IsToggled;
                try
                {
                    ContestoCassa.GestioneCatalogo.SalvaVariante(v);
                    salva.BackgroundColor = Ui.C("#1B5E20");
                    if (v.Categorie.Count == 0) await MessaggioBox.Avviso("VARIANTE SALVATA", $"{v.Nome} è salvata ma non compare in vendita:\nscegli almeno una categoria.");
                }
                catch (Exception ex) { await MessaggioBox.Errore("VARIANTE", ex.Message); }
            };
            var elimina = Ui.Tasto("🗑️", "#F44336", fontSize: 18, altezza: 44);
            elimina.WidthRequest = 52;
            View riquadro = null;
            elimina.Clicked += async (s, e) =>
            {
                if (v.Id > 0 && !await MessaggioBox.Conferma("ELIMINA VARIANTE", $"Eliminare la variante {v.Nome}?")) return;
                // toglie solo questa riga: le altre ➕ NUOVE non ancora salvate restano
                try { if (v.Id > 0) ContestoCassa.GestioneCatalogo.EliminaVariante(v.Id); (riquadro?.Parent as Layout)?.Remove(riquadro); }
                catch (Exception ex) { await MessaggioBox.Errore("VARIANTE", ex.Message); }
            };
            View[] celle = { nome, gruppo, Ui.Riquadro(tipo, "#FAFAFA", 6, new Thickness(8, 6), "#E0E0E0", 1), piu, meno, categorie, attiva, salva, elimina };
            for (int i = 0; i < celle.Length; i++) g.Add(celle[i], i, 0);
            riquadro = Ui.Riquadro(g, "#FFFFFF", 10, new Thickness(12, 6), "#E0E0E0", 1);
            return riquadro;
        }
    }
}
