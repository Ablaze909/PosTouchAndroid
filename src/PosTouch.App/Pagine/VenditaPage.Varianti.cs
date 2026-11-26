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
            var voci = disponibili.Select(v =>
            {
                int p = aggiunta ? v.PrezzoAggiuntaCent : v.PrezzoRimozioneCent;
                string prezzo = p > 0 ? $"   {(aggiunta ? "+" : "-")}€{p / 100m:N2}" : "";
                return $"{v.GruppoDisplay} • {v.Nome}{prezzo}";
            }).ToList();
            int i = await SceltaBox.Testi(aggiunta ? $"＋ AGGIUNGI VARIANTE — {target.Descrizione.Trim()}" : $"﹣ RIMUOVI VARIANTE — {target.Descrizione.Trim()}", voci, 2);
            if (i < 0) return;
            string err = Varianti.Applica(Carrello, target, disponibili[i], aggiunta, inCombinazione);
            if (err != null) { await MessaggioBox.Avviso("VARIANTI", err); return; }
            foreach (var r in Carrello.Righe) r.Selezionata = false;
            if (inCombinazione) AggiornaCombinazione();
        }

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
            catch (Exception ex) { await MessaggioBox.Errore("SPOSTA TAVOLO", ex.Message); return; }
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
