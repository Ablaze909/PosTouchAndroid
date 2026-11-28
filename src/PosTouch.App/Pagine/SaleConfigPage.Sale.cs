using System.Net;
using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;

namespace PosTouch.App.Pagine
{
    public partial class SaleConfigPage
    {
        private static readonly string[] Listini = { "Listino 1", "Listino 2", "Listino 3", "Listino 4" };

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
                            Campo(_c.NumeroSlotContiClienti.ToString(), t => _c.NumeroSlotContiClienti = int.TryParse(t, out var n) ? n : -1, Keyboard.Numeric, 90),
                            new Label { Text = "   Listino conti clienti:", FontSize = 15, VerticalOptions = LayoutOptions.Center },
                            InBox(Scelta("Listino conti clienti", Listini, _c.ListinoContiClienti - 1, i => _c.ListinoContiClienti = i + 1, 4))
                        }
                    },
                    new HorizontalStackLayout
                    {
                        Spacing = 10, Margin = new Thickness(0, 6, 0, 0),
                        Children =
                        {
                            new Label { Text = "⏱ Avviso tavolo fermo dopo (minuti):", FontSize = 15, VerticalOptions = LayoutOptions.Center },
                            Campo(_c.MinutiAvvisoTavolo.ToString(), t => _c.MinutiAvvisoTavolo = int.TryParse(t, out var n) ? n : 0, Keyboard.Numeric, 90)
                        }
                    },
                    Ui.Testo("Sulla mappa i tempi del tavolo diventano arancioni se dall'ultimo ordine o VAI è passato più di così (0 = mai).", 13, "#999999"),
                    Ui.Testo("Il coperto (se scelto) viene chiesto all'apertura di un tavolo nuovo. Qui compaiono solo gli articoli di tipo 🍽️ COPERTO (scheda articolo → Tipo articolo).", 13, "#999999")
                }
            }));
            // come il PC: si scelgono solo gli articoli segnati 🍽️ COPERTO (più quello già assegnato, per non perderlo);
            var tutti = ContestoCassa.Catalogo.TuttiProdotti.ToList();
            var assegnati = _c.Sale.Select(s => s.CopertoProdottoId).ToHashSet();
            var prodotti = tutti.Where(p => p.IsCoperto || assegnati.Contains(p.Id)).ToList();   // solo i COPERTI, come il PC
            var vociCoperto = new[] { "Nessuno" }.Concat(prodotti.Select(p => $"{p.Descrizione} ({p.CategoriaNome})")).ToList();

            var griglia = new Grid { ColumnSpacing = 8, RowSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(new GridLength(110)), new(new GridLength(120)), new(GridLength.Star), new(new GridLength(130)), new(GridLength.Auto) } };
            griglia.Add(Intestazione("NOME SALA"), 0, 0); griglia.Add(Intestazione("N. TAVOLI"), 1, 0);
            griglia.Add(Intestazione("PRIMO N."), 2, 0); griglia.Add(Intestazione("COPERTO"), 3, 0); griglia.Add(Intestazione("LISTINO"), 4, 0);
            int riga = 1;
            foreach (var sala in _c.Sale.ToList())
            {
                griglia.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                griglia.Add(Campo(sala.Nome, t => sala.Nome = t.Trim()), 0, riga);
                griglia.Add(Campo(sala.NumeroTavoli.ToString(), t => sala.NumeroTavoli = int.TryParse(t, out var n) ? n : -1, Keyboard.Numeric), 1, riga);
                griglia.Add(Campo(sala.NumeroIniziale.ToString(), t => sala.NumeroIniziale = int.TryParse(t, out var n) ? n : -1, Keyboard.Numeric), 2, riga);
                int ic = prodotti.FindIndex(p => p.Id == sala.CopertoProdottoId);
                griglia.Add(InBox(Scelta("Articolo coperto", vociCoperto, ic >= 0 ? ic + 1 : 0, i => sala.CopertoProdottoId = i > 0 ? prodotti[i - 1].Id : 0, 3)), 3, riga);
                // listino dei prezzi della sala (articoli, coperto, gusti): vale per quello che si batte da ora
                griglia.Add(InBox(Scelta($"Listino {sala.Nome}", Listini, Math.Clamp(sala.Listino, 1, 4) - 1, i => sala.Listino = i + 1, 4)), 4, riga);
                griglia.Add(TastoElimina(async () =>
                {
                    if (ContestoCassa.Tavoli.Aperti().Any(t => t.SalaId == sala.Id && t.Occupato))
                    { await MessaggioBox.Avviso("SALA CON TAVOLI APERTI", "Chiudi prima i tavoli aperti di questa sala."); return; }
                    if (!await MessaggioBox.Conferma("ELIMINA SALA", $"Eliminare la sala {sala.Nome}?")) return;
                    _c.Sale.Remove(sala);
                    Mostra(_scheda);
                }), 5, riga);
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
    }
}
