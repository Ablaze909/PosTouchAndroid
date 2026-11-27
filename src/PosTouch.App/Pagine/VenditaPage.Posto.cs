using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;
using PosTouch.Core.Sale;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    public partial class VenditaPage
    {
        // ══════════════ BARRA TAVOLO AUTODIMENSIONATA ══════════════

        private double _larghezzaBarra;

        /// <summary>
        /// Tasti in basso al tavolo: si dividono lo spazio solo quelli visibili (niente buchi se CONTO/PRECONTO
        /// o FUNZIONI/AVANZA sono spenti), CASSA un po' più larga; se stretti l'icona va sopra la scritta.
        /// </summary>
        private void SistemaBarraTavolo()
        {
            if (_larghezzaBarra <= 0) return;
            var tasti = BarraTavolo.Children.OfType<Button>().Where(b => b.IsVisible).ToList();
            if (tasti.Count == 0) return;
            _etichetteBarra ??= BarraTavolo.Children.OfType<Button>().ToDictionary(b => b, b => b.Text);
            BarraTavolo.ColumnDefinitions.Clear();
            double pesoTot = 0;
            for (int i = 0; i < tasti.Count; i++)
            {
                double peso = tasti[i].Text.Contains("CASSA") || _etichetteBarra[tasti[i]].Contains("CASSA") ? 1.6 : 1;
                pesoTot += peso;
                BarraTavolo.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(peso, GridUnitType.Star)));
                Grid.SetColumn(tasti[i], i);
                Grid.SetColumnSpan(tasti[i], 1);
            }
            double colonna = _larghezzaBarra / pesoTot;          // larghezza di un tasto "normale"
            bool dueRighe = colonna < 110;                        // stretto: icona sopra, scritta sotto
            foreach (var b in tasti)
            {
                var testo = _etichetteBarra[b];
                int sp = testo.IndexOf(' ');
                b.Text = dueRighe && sp > 0 ? testo[..sp] + "\n" + testo[(sp + 1)..] : testo;
                b.LineBreakMode = LineBreakMode.WordWrap;
                double lunghezza = (dueRighe && sp > 0 ? testo[(sp + 1)..] : testo).Length;
                double larg = testo.Contains("CASSA") ? colonna * 1.6 : colonna;
                b.FontSize = Math.Clamp(larg / (lunghezza * 0.62), 10, dueRighe ? 15 : 18);
                b.Padding = new Thickness(2, 0);
            }
        }

        private Dictionary<Button, string> _etichetteBarra;

        // ══════════════ DOVE SONO (banda in alto + totale colorato + tasto uscita) ══════════════

        /// <summary>
        /// Banco antracite, tavolo col colore della sua sala, conto cliente azzurro: banda in alto,
        /// riquadro del totale e tasto di uscita ("⬅ SALA" / "⬅ CONTI") dicono sempre dove sei.
        /// </summary>
        private void AggiornaPosto()
        {
            string colore, titolo, dettaglio;
            if (_sessione == null)
            {
                colore = TavoliPage.ColoreBanco; titolo = "🧾 BANCO"; dettaglio = "Vendita diretta";
                AvvisoSalvataggio.IsVisible = false;   // al banco non c'è tavolo da salvare
                BtnEsci.Text = "🚪"; BtnEsci.FontSize = 24;
            }
            else
            {
                var t = _sessione.Tavolo;
                colore = TavoliPage.ColoreDi(t.SalaId);
                var parti = new List<string>();
                if (_sessione.IsContoCliente) titolo = $"👤 {_sessione.NomeTavolo.ToUpperInvariant()}";
                else
                {
                    titolo = $"🍽️ {_sessione.NomeTavolo} · {_sessione.NomeSala.ToUpperInvariant()}";
                    int coperti = _sessione.NumeroCoperti;
                    if (coperti > 0) parti.Add($"{coperti} coperti");
                }
                parti.Add($"dalle {t.Apertura:HH:mm}");
                if (Cfg.Sale.AbilitaPortate && t.PortataCorrente > 0) parti.Add($"portata {t.PortataCorrente}");
                if (t.Preconto) parti.Add("PRECONTO");
                if (Carrello.Listino > 1) parti.Add($"LISTINO {Carrello.Listino}");   // come il PC: "[Listino 2]"
                dettaglio = string.Join(" · ", parti);
                // sempre rosso con bordo bianco (prima: colore della sala, si confondeva con la banda)
                BtnEsci.Text = _sessione.IsContoCliente ? "⬅\nCONTI" : "⬅\nSALA";
                BtnEsci.FontSize = 15;
            }
            var c = Color.FromArgb(colore);
            BandaPosto.BackgroundColor = c;
            BandaTitolo.Text = titolo;
            BandaDettaglio.Text = dettaglio;
            // totale nero su chiaro (leggibile con qualsiasi colore di sala): il colore resta nella striscia a sinistra
            StrisciaPosto.Color = c;
            TotalePosto.Text = titolo;
            TotalePosto.TextColor = Color.FromArgb("#37474F");
            EtichettaTotale.TextColor = EtichettaTotaleScritta.TextColor = Color.FromArgb("#111111");
            EtichettaPezzi.TextColor = Color.FromArgb("#607D8B");
        }
    }
}
