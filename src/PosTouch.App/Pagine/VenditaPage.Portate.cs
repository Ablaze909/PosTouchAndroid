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
        // ══════════════ PRECONTO / PORTATE ══════════════

        private async void OnPreconto(object sender, EventArgs e)
        {
            if (CombinazioneAperta()) return;
            if (_sessione == null || Attesa.IsVisible || _venditaAvviata) return;
            MostraAttesa(true, "⏳ Stampa preconto...");
            string errore;
            try { errore = await _sessione.PrecontoAsync(); }   // sul thread principale: legge il conto e salva in sicurezza (la stampa è già asincrona)
            finally { MostraAttesa(false); }
            if (errore != null) await MessaggioBox.Errore("PRECONTO", errore);
        }

        /// <summary>📑 FUNZIONI: scelta del separatore (⚡ SUBITO, SEGUE, SEGUE 2...) dove vanno i prossimi articoli.</summary>
        private async void OnFunzioniPortate(object sender, EventArgs e)
        {
            if (CombinazioneAperta()) return;
            if (_sessione == null) return;
            var separatori = Cfg.Sale.SeparatoriPortate;
            if (separatori == null || separatori.Count == 0)
            {
                await MessaggioBox.Avviso("NESSUN SEPARATORE", "Configura i separatori portate nelle impostazioni.");
                return;
            }
            var presenti = new HashSet<string>(Carrello.Righe.Where(r => r.Tipo == TipoRiga.Portata)
                                                             .Select(r => Portate.Nome(r).ToUpperInvariant()));
            var voci = new List<string> { presenti.Contains(Portate.NomeSubito) ? "⚡ SUBITO (vai a inizio)" : "⚡ SUBITO (aggiungi in testa)" };
            voci.AddRange(separatori.Select(s => presenti.Contains(s.Trim().ToUpperInvariant()) ? $"📌 {s} (vai a sezione)" : $"➕ {s}"));
            int i = await SceltaBox.Testi("📋 SCEGLI SEPARATORE", voci, 1);
            if (i < 0) return;
            Carrello.ScegliPortata(i == 0 ? Portate.NomeSubito : separatori[i - 1]);
            var sep = Carrello.Righe.LastOrDefault(r => r.Tipo == TipoRiga.Portata &&
                         (i == 0 ? Portate.IsSubito(r) : Portate.Nome(r).Equals(separatori[i - 1].Trim(), StringComparison.OrdinalIgnoreCase)));
            if (sep != null) ListaRighe.ScrollTo(sep, position: ScrollToPosition.Start, animate: false);
        }

        /// <summary>⏩ AVANZA: VAI con la prossima portata.</summary>
        private bool _avanzaInCorso;

        private async void OnAvanza(object sender, EventArgs e)
        {
            if (_avanzaInCorso) return;   // doppio tocco: una portata sola
            _avanzaInCorso = true;
            try { await AvanzaAsync(); }
            finally { _avanzaInCorso = false; }
        }

        private async Task AvanzaAsync()
        {
            if (CombinazioneAperta()) return;
            if (_sessione == null || Attesa.IsVisible) return;
            var a = _sessione.PreparaAvanza();
            if (!a.Possibile)
            {
                await MessaggioBox.Avviso(a.Titolo, a.Messaggio);
                return;
            }
            if (!a.SenzaStampa && !await MessaggioBox.Domanda(a.Titolo, a.Messaggio)) return;
            try
            {
                var esito = _sessione.EseguiAvanza(a);
                await MostraAvvisiAsync(esito);
            }
            catch (Exception ex)
            {
                await MessaggioBox.Errore("AVANZA", ex.Message);
            }
        }
    }
}
