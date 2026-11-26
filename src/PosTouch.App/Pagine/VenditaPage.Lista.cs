using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>VenditaPage — Lista scontrino: selezione, doppio tocco, lista espansa, frecce, totale.</summary>
    public partial class VenditaPage
    {
        // ══════════════ LISTA SCONTRINO: gesti ══════════════

        /// <summary>
        /// Tocco = seleziona/deseleziona (selezione multipla, come la ListView del PC).
        /// Doppio tocco sulla quantità = 🔢 QUANTITÀ; doppio tocco sull'importo = 💶 PREZZO UNITARIO.
        /// </summary>
        private async void OnRigaToccata(object sender, TappedEventArgs e)
        {
            if (sender is not View vista || vista.BindingContext is not RigaScontrino riga) return;

            bool doppio = ReferenceEquals(riga, _ultimaRigaToccata) && (DateTime.Now - _ultimoTocco).TotalMilliseconds < 400;
            _ultimaRigaToccata = riga;
            _ultimoTocco = DateTime.Now;

            if (!doppio)
            {
                riga.Selezionata = !riga.Selezionata;
                return;
            }

            // doppio tocco: annulla il cambio di selezione del primo tocco
            riga.Selezionata = !riga.Selezionata;
            _ultimaRigaToccata = null;
            if (riga.Tipo != TipoRiga.Prodotto) return;

            double x = e.GetPosition(vista)?.X ?? -1;
            double larghezza = vista.Width;
            if (x >= 0 && x <= 40) await ModificaQuantitaAsync(riga);
            else if (x >= larghezza - 80) await ModificaPrezzoAsync(riga);
        }

        private async Task ModificaQuantitaAsync(RigaScontrino riga)
        {
            if (!Cfg.AbilitaModificaVendita)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            var q = await TastierinoPrezzo.ChiediQuantita("🔢 QUANTITÀ", riga.Quantita);
            if (q == null) return;
            if (!Carrello.ImpostaQuantita(riga, q.Value))
                await MessaggioBox.Avviso("QUANTITÀ NON VALIDA", Carrello.UltimoErrore);
        }

        private async Task ModificaPrezzoAsync(RigaScontrino riga)
        {
            if (!Cfg.AbilitaCambioPrezzo)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            var p = await TastierinoPrezzo.ChiediEuro("💶 PREZZO UNITARIO", riga.PrezzoEuro);
            if (p == null) return;
            if (!Carrello.CambiaPrezzo(riga, (int)Math.Round(p.Value * 100, MidpointRounding.AwayFromZero)))
                await MessaggioBox.Avviso("PREZZO NON VALIDO", Carrello.UltimoErrore);
        }

        /// <summary>Tocco su 📋 SCONTRINO: lista espansa (nasconde la tastiera) / normale.</summary>
        private void OnIntestazioneScontrino(object sender, TappedEventArgs e) => ApplicaModalitaEspansa(!_scontrinoEspanso);
        private void OnChiudiLista(object sender, EventArgs e) => ApplicaModalitaEspansa(false);

        /// <summary>Lista espansa: cancella le righe selezionate (con un tocco sulla riga).</summary>
        private async void OnCancellaRigheEspanso(object sender, EventArgs e)
        {
            var selezionate = Carrello.Righe.Where(r => r.Selezionata).ToList();
            if (selezionate.Count == 0)
            {
                await MessaggioBox.Info("CANCELLA RIGHE", "Tocca le righe da cancellare per selezionarle, poi premi 🗑️ CANCELLA RIGHE.");
                return;
            }
            Carrello.Rimuovi(selezionate);
            if (Carrello.Vuoto) ApplicaModalitaEspansa(false);
        }

        private void ApplicaModalitaEspansa(bool espanso)
        {
            _scontrinoEspanso = espanso;
            TastieraNumerica.IsVisible = !espanso;
            BarraListaEspansa.IsVisible = espanso;
        }

        private void OnScrollSu(object sender, EventArgs e) => ScorriLista(-3);
        private void OnScrollGiu(object sender, EventArgs e) => ScorriLista(+3);

        private int _primaVisibile, _ultimaVisibile;

        private void ScorriLista(int righe)
        {
            if (Carrello.Righe.Count == 0) return;
            int indice = Math.Clamp(righe < 0 ? _primaVisibile + righe : _ultimaVisibile + righe, 0, Carrello.Righe.Count - 1);
            ListaRighe.ScrollTo(indice, position: righe < 0 ? ScrollToPosition.Start : ScrollToPosition.End, animate: true);
        }

        private void OnListaScrolled(object sender, ItemsViewScrolledEventArgs e)
        {
            _primaVisibile = e.FirstVisibleItemIndex;
            _ultimaVisibile = e.LastVisibleItemIndex;
            AggiornaFrecce();
        }

        private void AggiornaFrecce()
        {
            int n = Carrello.Righe.Count;
            BtnScrollSu.IsVisible = n > 0 && _primaVisibile > 0;
            BtnScrollGiu.IsVisible = n > 0 && _ultimaVisibile < n - 1 && _ultimaVisibile >= 0;
        }

        private void AggiornaTotale()
        {
            EtichettaTotale.Text = $"€ {Carrello.TotaleEuro:F2}".Replace('.', ',');
            EtichettaPezzi.Text = $"{Carrello.Pezzi} pz";
            if (Carrello.Righe.Count == 0) { _primaVisibile = 0; _ultimaVisibile = 0; }
            AggiornaFrecce();
        }

        private void SvuotaScontrino()
        {
            if (_scontrinoEspanso) ApplicaModalitaEspansa(false);
            Carrello.Svuota();
            _tastierino.Azzera();
        }
    }
}
