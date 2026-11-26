using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>VenditaPage — Tastiera numerica, C, X, €, sconti, annulla, ＋/﹣.</summary>
    public partial class VenditaPage
    {
        // ══════════════ TASTIERA ══════════════

        private void OnCifra(object sender, EventArgs e) => _tastierino.Premi(((Button)sender).Text);

        /// <summary>C del PC: azzera la tastiera; se non c'è nulla digitato cancella le righe selezionate, altrimenti l'ultima.</summary>
        private void OnCancella(object sender, EventArgs e)
        {
            if (_tastierino.Attivo)
            {
                _tastierino.Azzera();
                return;
            }
            if (CancellaInCombinazione()) return;
            var selezionate = Carrello.Righe.Where(r => r.Selezionata).ToList();
            if (selezionate.Count > 0)
            {
                Carrello.Rimuovi(selezionate);
            }
            else
            {
                if (_scontrinoEspanso) return; // lista espansa: C senza selezione non fa nulla
                var ultima = Carrello.Righe.LastOrDefault();
                if (ultima != null) Carrello.Rimuovi(new[] { ultima });
            }
        }

        private void OnPer(object sender, EventArgs e) => _tastierino.Moltiplica();

        private async void OnEuro(object sender, EventArgs e)
        {
            if (!Cfg.AbilitaCambioPrezzo)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            switch (_tastierino.Euro())
            {
                case EsitoTastoEuro.NonValido:
                    await MessaggioBox.Errore("PREZZO NON VALIDO", "Digita un importo valido!\n\nEsempio: 1,50 o 12,00");
                    break;
                case EsitoTastoEuro.Vuoto:
                    await MessaggioBox.Info("COME USARE IL TASTO €",
                        "Per impostare un prezzo personalizzato:\n\n1. Digita il prezzo (es: 1,50)\n2. Premi il tasto €\n3. Seleziona l'articolo");
                    break;
            }
        }

        private async void OnScontoValore(object sender, EventArgs e) => await ScontoAsync(percentuale: false);
        private async void OnScontoPercentuale(object sender, EventArgs e) => await ScontoAsync(percentuale: true);

        private async Task ScontoAsync(bool percentuale)
        {
            if (CombinazioneAperta()) return;
            if (ContestoCassa.ServizioVendita.InCorso) return;
            if (!Cfg.AbilitaSconti)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            if (Carrello.Vuoto)
            {
                await MessaggioBox.Avviso("SCONTRINO VUOTO", "Aggiungi prima dei prodotti!");
                return;
            }
            decimal subtotale = Carrello.SubtotaleProdottiCent / 100m;
            if (subtotale <= 0)
            {
                await MessaggioBox.Avviso("SUBTOTALE ZERO", "Non ci sono prodotti da scontare!");
                return;
            }
            if (Carrello.HaSconto)
            {
                await MessaggioBox.Avviso("SCONTO GIÀ PRESENTE", "È già presente uno sconto nello scontrino!\n\nRimuovilo prima di applicarne un altro.");
                return;
            }
            var valore = await ScontoBox.Chiedi(percentuale, subtotale);
            if (valore == null) return;
            if (percentuale) Carrello.ScontoPercentuale(valore.Value);
            else Carrello.ScontoValore((int)Math.Round(valore.Value * 100, MidpointRounding.AwayFromZero));
            ListaRighe.ScrollTo(Carrello.Righe.Count - 1, position: ScrollToPosition.End, animate: false);
        }

        private async void OnAnnulla(object sender, EventArgs e)
        {
            if (Carrello.Righe.Count == 0) return;
            if (!await MessaggioBox.Conferma("ANNULLA SCONTRINO", "Vuoi annullare tutto lo scontrino?\n\nQuesta operazione non può essere annullata."))
                return;
            if (_sessione != null)
            {
                // al tavolo: gli articoli già comandati vengono stornati all'uscita
                if (_scontrinoEspanso) ApplicaModalitaEspansa(false);
                Carrello.Rimuovi(Carrello.Righe.ToList());
                _tastierino.Azzera();
            }
            else SvuotaScontrino();
            if (_combinazione != null) EsciDaCombinazione();
        }

        private RigaScontrino PrimaSelezionata() => Carrello.Righe.FirstOrDefault(r => r.Selezionata);

        private async void OnPiu(object sender, EventArgs e)
        {
            var riga = PrimaSelezionata();
            if (riga?.Tipo == TipoRiga.Prodotto && !Carrello.ImpostaQuantita(riga, riga.Quantita + 1))
                await MessaggioBox.Avviso("QUANTITÀ", Carrello.UltimoErrore);
        }

        private async void OnMeno(object sender, EventArgs e)
        {
            var riga = PrimaSelezionata();
            if (riga?.Tipo == TipoRiga.Prodotto && riga.Quantita > 1 && !Carrello.ImpostaQuantita(riga, riga.Quantita - 1))
                await MessaggioBox.Avviso("QUANTITÀ", Carrello.UltimoErrore);
        }
    }
}
