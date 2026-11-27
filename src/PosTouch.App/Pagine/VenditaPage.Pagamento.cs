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
            if (CombinazioneAperta()) return;
            if (Carrello.Vuoto)
            {
                await MessaggioBox.Avviso("ATTENZIONE", "Lo scontrino è vuoto!");
                return;
            }
            OverlayPagamento.IsVisible = true;
        }

        private void OnChiudiPagamento(object sender, EventArgs e) => OverlayPagamento.IsVisible = false;

        /// <summary>📄 CONTO (btnConto_Click del PC): conto non fiscale sulla stampante termica.</summary>
        private async void OnConto(object sender, EventArgs e)
        {
            if (CombinazioneAperta()) return;
            if (_venditaAvviata || ContestoCassa.ServizioVendita.InCorso || ContestoCassa.ServizioConto.InCorso) return;
            if (!await TurnoValidoAsync()) return;
            if (Carrello.Vuoto)
            {
                await MessaggioBox.Avviso("ATTENZIONE", "Lo scontrino è vuoto!\n\nAggiungi almeno un prodotto prima di stampare il conto.");
                return;
            }
            _venditaAvviata = true;
            try
            {
                var righe = Carrello.Fotografia();
                int salaId = _sessione?.Tavolo.SalaId ?? ConfigStampe.SalaBanco;
                int eliminacode = _sessione == null ? ContestoCassa.ComandaBanco.Prepara(righe) : 0;   // comanda da banco
                MostraAttesa(true, "⏳ Stampa conto in corso...");
                PosTouch.Core.Conti.EsitoConto esito;
                try { esito = await Task.Run(() => ContestoCassa.ServizioConto.StampaAsync(righe, salaId, eliminacode)); }
                finally { MostraAttesa(false); }

                if (esito.Stato == PosTouch.Core.Conti.StatoConto.NonStampato)
                {
                    await MessaggioBox.Errore(esito.Titolo, esito.Messaggio);
                    return; // scontrino intatto
                }
                if (esito.Stato == PosTouch.Core.Conti.StatoConto.Incerto)
                {
                    bool uscito = await MessaggioBox.DomandaFiscale(esito.Titolo, esito.Messaggio + "\n\nIl conto è stato STAMPATO?");
                    if (!uscito)
                    {
                        await MessaggioBox.Avviso("CONTO NON SALVATO", "Il conto NON è stato salvato.\n\nLo scontrino è ancora visibile:\npuoi riprovare quando la stampante torna disponibile.");
                        return;
                    }
                    esito = ContestoCassa.ServizioConto.ConfermaContoStampato(esito);
                }

                // Conto stampato: al tavolo si chiude SUBITO (comande e liberazione) prima di qualsiasi messaggio
                var esitoTavolo = await ChiudiTavoloPagatoAsync(righe);
                SvuotaScontrino();
                await InviaComandaBancoAsync(righe, eliminacode);
                if (esito.Stato == PosTouch.Core.Conti.StatoConto.StampatoSenzaArchivio)
                    await MessaggioBox.Errore(esito.Titolo, esito.Messaggio);
                await DopoPagamentoAsync(esitoTavolo);
            }
            finally { _venditaAvviata = false; }
        }

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
            if (CombinazioneAperta()) return;   // avvisa subito, non dopo aver digitato il codice
            string codice = await TastierinoPrezzo.ChiediCodice("🔍 BARCODE");
            if (!string.IsNullOrWhiteSpace(codice)) AggiungiDaBarcode(codice.Trim());
        }

        // ══════════════ PAGAMENTO ══════════════

        /// <summary>CONTANTI dal popup CASSA: chiusura immediata, importo esatto (come il PC).</summary>
        private async void OnContanti(object sender, EventArgs e)
        {
            OverlayPagamento.IsVisible = false;
            if (Carrello.Vuoto) { await MessaggioBox.Avviso("ATTENZIONE", "Lo scontrino è vuoto!"); return; }
            if (Carrello.TotaleCent <= 0) { await MessaggioBox.Avviso("TOTALE NON VALIDO", $"Il totale è € {Carrello.TotaleCent / 100m:N2}: controlla prezzi e sconti."); return; }
            await EseguiVenditaAsync(Pagamento.Contanti(), 0);
        }

        /// <summary>PAG. ELETTRONICO dal popup CASSA: chiusura immediata su carta.</summary>
        private async void OnCarta(object sender, EventArgs e)
        {
            OverlayPagamento.IsVisible = false;
            if (Carrello.Vuoto) { await MessaggioBox.Avviso("ATTENZIONE", "Lo scontrino è vuoto!"); return; }
            if (Carrello.TotaleCent <= 0) { await MessaggioBox.Avviso("TOTALE NON VALIDO", $"Il totale è € {Carrello.TotaleCent / 100m:N2}: controlla prezzi e sconti."); return; }
            await EseguiVenditaAsync(Pagamento.Carta(), 0);
        }

        /// <summary>Tocco sulla barra TOTALE: calcolo resto con banconote/monete, carta o misto.</summary>
        private async void OnTotaleToccato(object sender, TappedEventArgs e)
        {
            if (CombinazioneAperta()) return;
            if (ContestoCassa.ServizioVendita.InCorso || Carrello.Vuoto) return;
            if (Carrello.TotaleCent <= 0) { await MessaggioBox.Avviso("TOTALE NON VALIDO", $"Il totale è € {Carrello.TotaleCent / 100m:N2}: controlla prezzi e sconti."); return; }
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

        /// <summary>Con la gestione operatori niente documento senza un turno aperto (la vendita non avrebbe operatore).</summary>
        private async Task<bool> TurnoValidoAsync()
        {
            if (ContestoCassa.Operatori.PuoLavorare) return true;
            await MessaggioBox.Avviso("TURNO NON APERTO", "Per emettere serve un turno aperto.\n\nTorna alla schermata iniziale e apri il turno con il tuo PIN.");
            return false;
        }

        private async Task EseguiVenditaAsync(Pagamento pagamento, int restoCent)
        {
            if (Carrello.Vuoto || ContestoCassa.ServizioVendita.InCorso || ContestoCassa.ServizioConto.InCorso || _venditaAvviata) return;
            if (!await TurnoValidoAsync()) return;
            _venditaAvviata = true;
            try { await EseguiVenditaInternaAsync(pagamento, restoCent); }
            finally { _venditaAvviata = false; }
        }

        private async Task EseguiVenditaInternaAsync(Pagamento pagamento, int restoCent)
        {
            var righe = Carrello.Fotografia();
            // comanda da banco: numero eliminacode in appendice allo scontrino (o su ticket della termica)
            int eliminacode = _sessione == null ? ContestoCassa.ComandaBanco.Prepara(righe) : 0;
            int appendice = ContestoCassa.ComandaBanco.InAppendice ? eliminacode : 0;
            MostraAttesa(true, "⏳ Vendita in corso...");
            EsitoVendita esito;
            try
            {
                // Fuori dal thread grafico: la comunicazione col registratore non deve bloccare lo schermo
                esito = await Task.Run(() => ContestoCassa.ServizioVendita.EmettiAsync(righe, pagamento, appendice));
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
                bool uscito = await MessaggioBox.DomandaFiscale(esito.Titolo, esito.Messaggio + "\n\nLo scontrino è stato STAMPATO?");
                if (!uscito)
                {
                    await MessaggioBox.Avviso("SCONTRINO NON EMESSO",
                        "Lo scontrino resta a video.\nAlla prossima vendita il registratore verrà controllato e sbloccato automaticamente.");
                    return;
                }
                esito = ContestoCassa.ServizioVendita.ConfermaScontrinoUscito(esito);
            }

            // al tavolo si chiude SUBITO (comande e liberazione): se l'app viene chiusa sul messaggio del resto non si perde nulla
            var esitoTavolo = await ChiudiTavoloPagatoAsync(righe);
            // comanda da banco accodata subito dopo lo scontrino (prima di svuotare): se l'app si chiude non si perde
            await InviaComandaBancoAsync(righe, eliminacode);
            SvuotaScontrino();
            if (restoCent > 0) MostraRestoConTimer(restoCent);
            if (esito.Stato == StatoEsito.RiuscitoSenzaArchivio)
                await MessaggioBox.Avviso("ATTENZIONE", esito.Messaggio);
            await DopoPagamentoAsync(esitoTavolo, restoCent);
        }

        /// <summary>Documento al banco uscito: accoda la comanda con l'eliminacode (avvisa se un'uscita non ha stampante).</summary>
        private static async Task InviaComandaBancoAsync(List<RigaScontrino> righe, int eliminacode)
        {
            if (eliminacode <= 0) return;
            List<string> avvisi;
            try { avvisi = await Task.Run(() => ContestoCassa.ComandaBanco.Invia(righe, eliminacode)); }
            catch (Exception ex)
            {
                // lo scontrino è già uscito: un errore qui non deve fermare la chiusura della vendita
                PosTouch.Core.Diagnostica.LogManager.Error($"Comanda banco N. {eliminacode} non accodata: {ex}");
                await MessaggioBox.Errore("⚠️ COMANDA BANCO", $"La comanda N. {eliminacode} non è stata mandata in produzione:\n{ex.Message}\n\nAvvisa la cucina/il bar.");
                return;
            }
            if (avvisi.Count > 0)
                await MessaggioBox.Avviso("⚠️ COMANDA BANCO", string.Join("\n\n", avvisi) + "\n\nImposta le stampanti in IMPOSTAZIONI → 🖨️ STAMPE.");
        }

        /// <summary>RESTO arancione nel riquadro del totale: resta finché non si batte l'articolo dopo (o si cambia posto).</summary>
        private void MostraRestoConTimer(int restoCent)
        {
            EtichettaResto.Text = $"€ {restoCent / 100m:F2}".Replace('.', ',');
            BarraResto.IsVisible = true;
            AggiornaRigaPosto();
        }

        private void NascondiResto()
        {
            if (!BarraResto.IsVisible) return;
            BarraResto.IsVisible = false;
            AggiornaRigaPosto();
        }

        private void MostraAttesa(bool visibile, string testo = null)
        {
            if (testo != null) TestoAttesa.Text = testo;
            Attesa.IsVisible = visibile;
        }
    }
}
