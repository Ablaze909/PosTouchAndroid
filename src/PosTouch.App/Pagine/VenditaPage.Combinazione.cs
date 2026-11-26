using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// VenditaPage — articolo a combinazione (pizza multi-gusto), come MainWindow del PC:
    /// tocco sull'articolo → griglia con i gusti della stessa categoria e banner arancione;
    /// ✅ CHIUDI COMBINAZIONE calcola il prezzo, ❌ ANNULLA toglie tutto. Finché è aperta
    /// non si chiude lo scontrino, non si cambia categoria e non si esce (CombinazioneApertaBlocca).
    /// </summary>
    public partial class VenditaPage
    {
        private CombinazioneInCorso _combinazione;

        private async Task AvviaCombinazioneAsync(Prodotto p)
        {
            _tastierino.Azzera();
            var comb = CombinazioneInCorso.Inizia(Carrello, p, ContestoCassa.Catalogo.ProdottiDi(p.CategoriaId), out string errore);
            if (comb == null) { await MessaggioBox.Avviso("NESSUN GUSTO", errore); return; }
            _combinazione = comb;
            MostraInFondo(comb.Padre);
            AggiornaCombinazione();
        }

        private async Task AggiungiGustoAsync(Prodotto gusto)
        {
            _tastierino.Azzera();
            string errore = _combinazione.AggiungiGusto(gusto);
            if (errore != null)
            {
                await MessaggioBox.Avviso(_combinazione.Completa ? "GUSTI COMPLETI" : "GUSTO NON VALIDO", errore);
                return;
            }
            var ultimo = Carrello.FigliDi(_combinazione.Padre).LastOrDefault();
            if (ultimo != null) MostraInFondo(ultimo);
            AggiornaCombinazione();
        }

        private async void OnChiudiCombinazione(object sender, EventArgs e)
        {
            if (_combinazione == null) return;
            string errore = _combinazione.Chiudi();
            if (errore != null) { await MessaggioBox.Avviso("COMBINAZIONE INCOMPLETA", errore); return; }
            EsciDaCombinazione();
        }

        private void OnAnnullaCombinazione(object sender, EventArgs e)
        {
            if (_combinazione == null) return;
            _combinazione.Annulla();
            EsciDaCombinazione();
        }

        /// <summary>Tasto C durante la combinazione: toglie l'ultimo gusto.</summary>
        private bool CancellaInCombinazione()
        {
            if (_combinazione == null) return false;
            _combinazione.TogliUltimoGusto();
            AggiornaCombinazione();
            return true;
        }

        private void EsciDaCombinazione()
        {
            _combinazione = null;
            AggiornaCombinazione();
        }

        private void AggiornaCombinazione()
        {
            // combinazione cancellata da fuori (es. ANNULLA scontrino): si chiude la modalità
            if (_combinazione != null && !_combinazione.Presente) _combinazione = null;
            BannerCombinazione.IsVisible = _combinazione != null;
            if (_combinazione != null) TestoCombinazione.Text = _combinazione.Banner;
            MostraArticoli();
        }

        /// <summary>CombinazioneApertaBlocca del PC: true (e avviso) se c'è una combinazione da chiudere.</summary>
        private bool CombinazioneAperta()
        {
            if (_combinazione != null && !_combinazione.Presente) EsciDaCombinazione();
            if (_combinazione == null) return false;
            _ = MessaggioBox.Avviso("COMBINAZIONE APERTA", "Completa la combinazione con ✅ CHIUDI COMBINAZIONE\noppure annullala con ❌ ANNULLA.");
            return true;
        }
    }
}
