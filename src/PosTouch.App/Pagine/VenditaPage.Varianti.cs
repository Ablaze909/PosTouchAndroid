using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// VenditaPage — VAR＋ / VAR﹣ (varianti della categoria sull'articolo selezionato o sull'ultimo)
    /// e, al tavolo, 🔀 SPOSTA / 🔗 UNISCI (come VenditaContoClienteDialog del PC).
    /// </summary>
    public partial class VenditaPage
    {
        private void OnVariantePiu(object sender, EventArgs e) => _ = ApplicaVarianteAsync(true);
        private void OnVarianteMeno(object sender, EventArgs e) => _ = ApplicaVarianteAsync(false);

        private async Task ApplicaVarianteAsync(bool aggiunta)
        {
            if (ContestoCassa.ServizioVendita.InCorso || _venditaAvviata) return;
            bool inCombinazione = _combinazione != null && _combinazione.Presente;
            var target = Varianti.TrovaTarget(Carrello, PrimaSelezionata(), inCombinazione, out string errore);
            if (target == null) { await MessaggioBox.Avviso("VARIANTI", errore); return; }

            int categoria = target.CategoriaId;
            var disponibili = categoria > 0 ? ContestoCassa.Catalogo.VariantiDi(categoria) : new List<Variante>();
            if (disponibili.Count == 0)
            {
                await MessaggioBox.Avviso("VARIANTI", "Nessuna variante configurata per questa categoria.\n\nCreale in BACK OFFICE → 🧩 VARIANTI.");
                return;
            }
            var scelte = await VariantiBox.Scegli(disponibili, aggiunta, target.Descrizione.Trim());
            if (scelte == null || scelte.Count == 0) return;
            if (ContestoCassa.ServizioVendita.InCorso || _venditaAvviata) return;
            if (!Carrello.Righe.Contains(target)) return;   // il carrello è cambiato nel frattempo

            // più pezzi sulla stessa riga: la variante vale per tutti o per uno solo?
            if (PuoSeparare(target))
            {
                int pezzi = (int)target.Quantita;
                int k = await SceltaBox.Testi($"{target.Descrizione.Trim()} × {pezzi}", new List<string> { "SOLO 1 PEZZO", $"TUTTI I {pezzi} PEZZI" }, 2);
                if (k < 0) return;
                if (k == 0) target = Carrello.SeparaUnPezzo(target) ?? target;
            }
            // "combinazione aperta" vale solo per i gusti di QUELLA combinazione: un gusto di una pizza già chiusa va ricalcolato
            bool dellaAperta = inCombinazione && (target.Tipo != TipoRiga.Gusto || Carrello.PadreDi(target) == _combinazione.Padre);
            string err = Varianti.ApplicaTutte(Carrello, target, scelte, aggiunta, dellaAperta);
            if (err != null) await MessaggioBox.Avviso("VARIANTI", err);
            foreach (var r in Carrello.Righe) r.Selezionata = false;
            if (inCombinazione) AggiornaCombinazione();
        }

        /// <summary>Articolo normale con più pezzi interi (non a peso, non combinazione, non comandato).</summary>
        private bool PuoSeparare(RigaScontrino r) =>
            r.Tipo == TipoRiga.Prodotto && !r.Comandata && !r.IsAPeso && r.Quantita >= 2 && r.Quantita == decimal.Truncate(r.Quantita)
            && !Carrello.FigliDi(r).Any(f => f.Tipo == TipoRiga.Gusto);

        // ══════════════ 🔀 SPOSTA / 🔗 UNISCI ══════════════

        private async void OnSposta(object sender, EventArgs e)
        {
            if (_sessione == null || CombinazioneAperta() || Attesa.IsVisible) return;
            var scelta = await SceltaTavoloBox.Sposta(_sessione.Tavolo.SalaId, _sessione.Tavolo.Numero);
            if (scelta == null) return;
            var (sala, numero, nome) = scelta.Value;
            _timerBozza?.Stop();
            string esito;
            try { esito = _sessione.Sposta(sala, numero, nome); }
            catch (Exception ex) { ProgrammaBozza(); await MessaggioBox.Errore("SPOSTA TAVOLO", ex.Message); return; }   // il tavolo resta aperto: torna a salvarsi
            CollegaCarrello(null);
            await MessaggioBox.Info("🔀 TAVOLO SPOSTATO", esito);
            await Navigazione.Apri(this, () => new TavoliPage());
        }

        private async void OnUnisci(object sender, EventArgs e)
        {
            if (_sessione == null || CombinazioneAperta() || Attesa.IsVisible) return;
            var ids = await SceltaTavoloBox.Unisci(_sessione.Tavolo.SalaId, _sessione.Tavolo.Numero);
            if (ids == null || ids.Count == 0) return;
            _timerBozza?.Stop();
            try { _sessione.Unisci(ids); }
            catch (Exception ex) { await MessaggioBox.Errore("UNISCI CONTI", ex.Message); return; }
            await MessaggioBox.Info("🔗 CONTI UNITI", $"{ids.Count} tavolo/i uniti in {_sessione.Titolo}.");
        }
    }
}
