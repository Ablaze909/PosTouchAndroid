using System.ComponentModel;
using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    public partial class BackOfficePage
    {
        // ══════════════ PRODOTTI ══════════════

        private IDispatcherTimer _timerRicerca;

        /// <summary>
        /// Ricerca: mentre si scrive si aspetta una pausa di 350 ms e poi si filtra UNA volta
        /// (prima: a ogni lettera tutto il catalogo filtrato e la lista ridisegnata). Il filtro categoria resta immediato.
        /// </summary>
        private void OnFiltroCambiato(object sender, EventArgs e)
        {
            if (sender is not Entry) { _timerRicerca?.Stop(); FiltraProdotti(); return; }
            if (_timerRicerca == null)
            {
                _timerRicerca = Dispatcher.CreateTimer();
                _timerRicerca.Interval = TimeSpan.FromMilliseconds(350);
                _timerRicerca.IsRepeating = false;
                _timerRicerca.Tick += (s, a) => FiltraProdotti();
            }
            _timerRicerca.Stop();
            _timerRicerca.Start();
        }

        private void OnChipPreferiti(object sender, TappedEventArgs e)
        {
            _soloPreferiti = !_soloPreferiti;
            ChipPreferiti.Stroke = new SolidColorBrush(_soloPreferiti ? Ui.C("#F57F17") : Colors.Transparent);
            FiltraProdotti();
        }

        private void OnChipNascosti(object sender, TappedEventArgs e)
        {
            _soloNascosti = !_soloNascosti;
            ChipNascosti.Stroke = new SolidColorBrush(_soloNascosti ? Ui.C("#C62828") : Colors.Transparent);
            FiltraProdotti();
        }

        private void FiltraProdotti()
        {
            if (FiltroCategoria == null || ListaProdotti == null) return;
            int catId = CategoriaFiltro;
            string testo = (CampoRicerca.Text ?? "").Trim();
            string barcode = (CampoBarcode.Text ?? "").Trim();

            var selezionati = new HashSet<int>(_voci.Where(v => v.Selezionato).Select(v => v.P.Id));
            _voci = ContestoCassa.Catalogo.TuttiProdotti
                .Where(p => catId == 0 || p.CategoriaId == catId)
                .Where(p => testo.Length == 0 || p.Descrizione.Contains(testo, StringComparison.OrdinalIgnoreCase) || p.Codice.Contains(testo))
                .Where(p => barcode.Length == 0 || (p.Barcode ?? "").Contains(barcode)
                            || (p.BarcodeAggiuntivi?.Any(b => (b.Barcode ?? "").Contains(barcode)) ?? false))   // anche i barcode delle confezioni
                .Where(p => !_soloPreferiti || p.IsPreferito)
                .Where(p => !_soloNascosti || !p.Attivo)
                .Select(p => new VoceProdotto { P = p, Selezionato = selezionati.Contains(p.Id) })
                .ToList();
            ListaProdotti.ItemsSource = _voci;
            AggiornaBarraAzioni();
        }

        private DateTime _toccoSpunta = DateTime.MinValue;

        private async void OnProdottoToccato(object sender, TappedEventArgs e)
        {
            await Task.Delay(60); // lascia passare prima l'eventuale tocco sulla spunta ☐
            if ((DateTime.Now - _toccoSpunta).TotalMilliseconds < 400) return;
            if ((sender as BindableObject)?.BindingContext is VoceProdotto v)
                await Navigazione.Apri(this, () => new ProdottoPage(v.P, v.P.CategoriaId));
        }

        private void OnSpuntaToccata(object sender, TappedEventArgs e)
        {
            _toccoSpunta = DateTime.Now;
            if ((sender as BindableObject)?.BindingContext is not VoceProdotto v) return;
            v.Selezionato = !v.Selezionato;
            AggiornaBarraAzioni();
        }

        private void OnSelezionaTutti(object sender, TappedEventArgs e)
        {
            bool tutti = !_voci.All(v => v.Selezionato);
            foreach (var v in _voci) v.Selezionato = tutti;
            AggiornaBarraAzioni();
        }

        private List<int> Selezionati() => _voci.Where(v => v.Selezionato).Select(v => v.P.Id).ToList();

        private void AggiornaBarraAzioni()
        {
            int n = _voci.Count(v => v.Selezionato);
            BarraAzioni.IsVisible = n > 0;
            TestoSelezionati.Text = n == 1 ? "1 selezionato" : $"{n} selezionati";
        }

        private async void OnNuovoProdotto(object sender, EventArgs e)
        {
            var categorie = ContestoCassa.Catalogo.TutteCategorie;
            if (categorie.Count == 0)
            {
                await MessaggioBox.Avviso("NESSUNA CATEGORIA", "Crea prima una categoria nella scheda 🏷️ CATEGORIE.");
                return;
            }
            int catId = CategoriaFiltro;
            await Navigazione.Apri(this, () => new ProdottoPage(null, catId > 0 ? catId : categorie[0].Id));
        }

        private async void OnSposta(object sender, EventArgs e)
        {
            var categorie = ContestoCassa.Catalogo.TutteCategorie.ToList();
            int i = await SceltaBox.Testi("📂 SPOSTA IN CATEGORIA", categorie.Select(c => c.Nome).ToList());
            if (i < 0) return;
            await Esegui(() => ContestoCassa.GestioneCatalogo.SpostaProdotti(Selezionati(), categorie[i].Id));
        }

        private async void OnNascondi(object sender, EventArgs e) => await Esegui(() => ContestoCassa.GestioneCatalogo.ImpostaAttivo(Selezionati(), false));
        private async void OnMostra(object sender, EventArgs e) => await Esegui(() => ContestoCassa.GestioneCatalogo.ImpostaAttivo(Selezionati(), true));

        private async void OnElimina(object sender, EventArgs e)
        {
            var ids = Selezionati();
            if (!await MessaggioBox.Conferma("🗑️ ELIMINA", $"Eliminare definitivamente {ids.Count} prodotti?\n\nQuesta operazione non può essere annullata.")) return;
            await Esegui(() => ContestoCassa.GestioneCatalogo.EliminaProdotti(ids));
            foreach (var v in _voci) v.Selezionato = false;
        }

        private async Task Esegui(Action azione)
        {
            try
            {
                await Task.Run(azione);
                Ricarica();
            }
            catch (Exception ex)
            {
                await MessaggioBox.Errore("ERRORE", ex.Message);
            }
        }
    }
}
