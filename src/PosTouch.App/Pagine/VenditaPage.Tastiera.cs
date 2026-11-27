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
        private async void OnCancella(object sender, EventArgs e)
        {
            if (Occupata) return;
            if (_tastierino.Attivo)
            {
                _tastierino.Azzera();
                _prezzoAutorizzato = false;
                return;
            }
            if (CancellaInCombinazione()) return;
            var selezionate = Carrello.Righe.Where(r => r.Selezionata).ToList();
            if (selezionate.Count > 0)
            {
                if (!await ConfermaStornoAsync(selezionate)) return;
                Carrello.Rimuovi(selezionate);
                if (Carrello.UltimoErrore != null) _ = MessaggioBox.Avviso("ELIMINA", Carrello.UltimoErrore);
            }
            else
            {
                if (_scontrinoEspanso) return; // lista espansa: C senza selezione non fa nulla
                var ultima = Carrello.Righe.LastOrDefault();
                if (ultima == null) return;
                if (!await ConfermaStornoAsync(new[] { ultima })) return;
                Carrello.Rimuovi(new[] { ultima });
                if (Carrello.UltimoErrore != null) _ = MessaggioBox.Avviso("ELIMINA", Carrello.UltimoErrore);
            }
        }

        /// <summary>
        /// Vendita/conto in emissione: la schermata è sotto la copertura ma per sicurezza ogni tasto che cambia
        /// lo scontrino o apre altre pagine si ferma qui (lo scontrino deve restare quello fotografato).
        /// </summary>
        private bool Occupata => Attesa.IsVisible || _venditaAvviata || ContestoCassa.ServizioVendita.InCorso || ContestoCassa.ServizioConto.InCorso;

        /// <summary>Al tavolo, C su un articolo già in cucina lo storna: si chiede prima (un tocco sbagliato stamperebbe uno storno).</summary>
        private async Task<bool> ConfermaStornoAsync(IEnumerable<RigaScontrino> righe)
        {
            // un gusto o una variante cancellati portano via il loro articolo (Carrello.Rimuovi risale al padre)
            var elenco0 = righe.ToList();
            // varianti di un articolo già in cucina: non si tolgono da sole (si storna l'articolo intero) → avviso subito, niente PIN
            // (se l'articolo stesso è tra le righe scelte si storna tutto: va bene)
            if (_sessione != null && elenco0.Any(r => r.Tipo == TipoRiga.Variante && Carrello.ProprietarioDi(r) is { Tipo: TipoRiga.Prodotto, Comandata: true } art && !elenco0.Contains(art)))
            {
                await MessaggioBox.Avviso("VARIANTE GIÀ IN CUCINA", "Le varianti di un articolo già inviato in cucina non si tolgono.\n\nElimina l'articolo (verrà stornato) e battilo di nuovo.");
                return false;
            }
            var comandate = elenco0.Select(r => r.Tipo == TipoRiga.Prodotto ? r : Carrello.PadreDi(r) ?? r)
                                 .Where(r => r.Tipo == TipoRiga.Prodotto && r.Comandata).Distinct().ToList();
            if (_sessione == null || comandate.Count == 0) return true;
            string elenco = string.Join("\n", comandate.Take(5).Select(r => $"• {r.Quantita:0.##} x {r.Descrizione}")) + (comandate.Count > 5 ? "\n…" : "");
            return await MessaggioBox.Conferma("STORNO ARTICOLI",
                       $"Questi articoli sono già stati inviati in cucina:\n\n{elenco}\n\nEliminandoli verrà stampato lo storno all'uscita dal tavolo.")
                   && await Accesso.VerificaAsync(Permesso.StornoComandati, "STORNO");
        }

        private void OnPer(object sender, EventArgs e) => _tastierino.Moltiplica();

        /// <summary>Prezzo battuto con € già autorizzato (PIN di un altro operatore): non si richiede di nuovo sull'articolo.</summary>
        private bool _prezzoAutorizzato;

        private async void OnEuro(object sender, EventArgs e)
        {
            if (!Cfg.AbilitaCambioPrezzo)
            {
                await MessaggioBox.Avviso("FUNZIONE DISABILITATA", "Funzione disabilitata dall'amministratore.");
                return;
            }
            if (_tastierino.Attivo && !await Accesso.VerificaAsync(Permesso.ModificaPrezzi, "CAMBIO PREZZO")) { _tastierino.Azzera(); _prezzoAutorizzato = false; return; }
            var esitoEuro = _tastierino.Euro();
            _prezzoAutorizzato = esitoEuro == EsitoTastoEuro.Impostato;
            switch (esitoEuro)
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

        /// <summary>Un solo tasto SCONTO: prima i controlli (scontrino vuoto, sconto già presente…), poi la finestra (% o € si sceglie lì).</summary>
        private async void OnSconto(object sender, EventArgs e) => await ScontoAsync();

        private async Task ScontoAsync()
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
            if (!await Accesso.VerificaAsync(Permesso.Sconti, "SCONTO")) return;
            var scelta = await ScontoBox.Chiedi(subtotale);
            if (scelta == null) return;
            var (percentuale, valore) = scelta.Value;
            bool ok = percentuale ? Carrello.ScontoPercentuale(valore)
                                  : Carrello.ScontoValore((int)Math.Round(valore * 100, MidpointRounding.AwayFromZero));
            if (!ok) { await MessaggioBox.Avviso("SCONTO", Carrello.UltimoErrore ?? "Sconto non applicabile."); return; }
            MostraInFondo(Carrello.Righe.LastOrDefault());
        }

        private async void OnAnnulla(object sender, EventArgs e)
        {
            if (Occupata || Carrello.Righe.Count == 0) return;
            if (!await MessaggioBox.Conferma("ANNULLA SCONTRINO", "Vuoi annullare tutto lo scontrino?\n\nQuesta operazione non può essere annullata."))
                return;
            if (!await Accesso.VerificaAsync(Permesso.AnnullaScontrino, "ANNULLA SCONTRINO")) return;
            // al tavolo gli articoli già in cucina vengono stornati: serve anche quel permesso
            if (_sessione != null && Carrello.Righe.Any(r => r.Tipo == TipoRiga.Prodotto && r.Comandata)
                && !await Accesso.VerificaAsync(Permesso.StornoComandati, "STORNO")) return;
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
            if (Occupata) return;
            var riga = PrimaSelezionata();
            if (riga?.IsAPeso == true) return;   // il peso non si cambia a pezzi: si riprende sulla bilancia
            if (riga?.Tipo == TipoRiga.Prodotto && !Carrello.ImpostaQuantita(riga, riga.Quantita + 1))
                await MessaggioBox.Avviso("QUANTITÀ", Carrello.UltimoErrore);
        }

        private async void OnMeno(object sender, EventArgs e)
        {
            if (Occupata) return;
            var riga = PrimaSelezionata();
            if (riga?.IsAPeso == true) return;
            if (riga?.Tipo == TipoRiga.Prodotto && riga.Quantita > 1 && !Carrello.ImpostaQuantita(riga, riga.Quantita - 1))
                await MessaggioBox.Avviso("QUANTITÀ", Carrello.UltimoErrore);
        }
    }
}
