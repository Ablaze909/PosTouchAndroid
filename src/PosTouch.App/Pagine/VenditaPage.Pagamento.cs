using PosTouch.App.Controlli;
using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>VenditaPage — Barra inferiore, popup CASSA, calcolo resto ed emissione dello scontrino.</summary>
    public partial class VenditaPage
    {
        // ══════════════ BARRA INFERIORE ══════════════

        private async void OnCassa(object sender, EventArgs e)
        {
            if (Carrello.Vuoto)
            {
                await MessaggioBox.Avviso("ATTENZIONE", "Lo scontrino è vuoto!");
                return;
            }
            OverlayPagamento.IsVisible = true;
        }

        private void OnChiudiPagamento(object sender, EventArgs e) => OverlayPagamento.IsVisible = false;

        private async void OnConto(object sender, EventArgs e)
            => await MessaggioBox.Info("📄 CONTO", "La gestione dei conti (parcheggio e conti clienti) arriverà nella prossima versione.");

        private bool _cassettoInCorso;

        private async void OnCassetto(object sender, EventArgs e)
        {
            if (_cassettoInCorso || ContestoCassa.ServizioVendita.InCorso) return;
            _cassettoInCorso = true;
            try
            {
                var esito = await Task.Run(() => ContestoCassa.FunzioniCassa.ApriCassettoAsync());
                if (!esito.Ok) await MessaggioBox.Errore("CASSETTO", esito.Messaggio);
            }
            finally { _cassettoInCorso = false; }
        }

        private async void OnBarcode(object sender, EventArgs e)
        {
            string codice = await TastierinoPrezzo.ChiediCodice("🔍 BARCODE");
            if (!string.IsNullOrWhiteSpace(codice)) AggiungiDaBarcode(codice.Trim());
        }

        // ══════════════ PAGAMENTO ══════════════

        /// <summary>CONTANTI dal popup CASSA: chiusura immediata, importo esatto (come il PC).</summary>
        private async void OnContanti(object sender, EventArgs e)
        {
            OverlayPagamento.IsVisible = false;
            if (Carrello.Vuoto) { await MessaggioBox.Avviso("ATTENZIONE", "Lo scontrino è vuoto!"); return; }
            if (Carrello.TotaleCent <= 0) return;
            await EseguiVenditaAsync(Pagamento.Contanti(), 0);
        }

        /// <summary>PAG. ELETTRONICO dal popup CASSA: chiusura immediata su carta.</summary>
        private async void OnCarta(object sender, EventArgs e)
        {
            OverlayPagamento.IsVisible = false;
            if (Carrello.Vuoto) { await MessaggioBox.Avviso("ATTENZIONE", "Lo scontrino è vuoto!"); return; }
            if (Carrello.TotaleCent <= 0) return;
            await EseguiVenditaAsync(Pagamento.Carta(), 0);
        }

        /// <summary>Tocco sulla barra TOTALE: calcolo resto con banconote/monete, carta o misto.</summary>
        private async void OnTotaleToccato(object sender, TappedEventArgs e)
        {
            if (Carrello.TotaleCent <= 0 || ContestoCassa.ServizioVendita.InCorso) return;
            var esito = await CalcoloResto.Mostra(Carrello.TotaleEuro);
            if (esito == null) return;

            var pagamento = esito.Metodo switch
            {
                MetodiPagamento.Misto => Pagamento.Misto(esito.ContantiCent),
                MetodiPagamento.Carta => Pagamento.Carta(),
                _ => Pagamento.Contanti(esito.RicevutoCent > Carrello.TotaleCent ? esito.RicevutoCent : 0)
            };
            await EseguiVenditaAsync(pagamento, esito.RestoCent);
        }

        private bool _venditaAvviata;

        private async Task EseguiVenditaAsync(Pagamento pagamento, int restoCent)
        {
            if (Carrello.Vuoto || ContestoCassa.ServizioVendita.InCorso || _venditaAvviata) return;
            _venditaAvviata = true;
            try { await EseguiVenditaInternaAsync(pagamento, restoCent); }
            finally { _venditaAvviata = false; }
        }

        private async Task EseguiVenditaInternaAsync(Pagamento pagamento, int restoCent)
        {
            var righe = Carrello.Fotografia();
            MostraAttesa(true, "⏳ Vendita in corso...");
            EsitoVendita esito;
            try
            {
                // Fuori dal thread grafico: la comunicazione col registratore non deve bloccare lo schermo
                esito = await Task.Run(() => ContestoCassa.ServizioVendita.EmettiAsync(righe, pagamento));
            }
            finally
            {
                MostraAttesa(false);
            }

            if (esito.Stato == StatoEsito.Fallito)
            {
                await MessaggioBox.Errore(esito.Titolo, esito.Messaggio);
                return; // scontrino intatto: l'operatore può riprovare
            }
            if (esito.Stato == StatoEsito.Incerto)
            {
                // Comunicazione interrotta durante la chiusura: decide l'operatore guardando l'RT (evita doppi scontrini)
                bool uscito = await MessaggioBox.Domanda(esito.Titolo, esito.Messaggio + "\n\nLo scontrino è stato STAMPATO?");
                if (!uscito)
                {
                    await MessaggioBox.Avviso("SCONTRINO NON EMESSO",
                        "Lo scontrino resta a video.\nAlla prossima vendita il registratore verrà controllato e sbloccato automaticamente.");
                    return;
                }
                esito = ContestoCassa.ServizioVendita.ConfermaScontrinoUscito(esito);
            }

            SvuotaScontrino();
            if (restoCent > 0) MostraRestoConTimer(restoCent);
            if (esito.Stato == StatoEsito.RiuscitoSenzaArchivio)
                await MessaggioBox.Avviso("ATTENZIONE", esito.Messaggio);
        }

        /// <summary>Barra RESTO arancione per 3 secondi (MostraRestoConTimer del PC).</summary>
        private void MostraRestoConTimer(int restoCent)
        {
            EtichettaResto.Text = $"€ {restoCent / 100m:F2}".Replace('.', ',');
            BarraResto.IsVisible = true;
            _timerResto?.Stop();
            _timerResto = Dispatcher.CreateTimer();
            _timerResto.Interval = TimeSpan.FromSeconds(3);
            _timerResto.IsRepeating = false;
            _timerResto.Tick += (s, e) => NascondiResto();
            _timerResto.Start();
        }

        private void NascondiResto()
        {
            _timerResto?.Stop();
            BarraResto.IsVisible = false;
        }

        private void MostraAttesa(bool visibile, string testo = null)
        {
            if (testo != null) TestoAttesa.Text = testo;
            Attesa.IsVisible = visibile;
        }
    }
}
