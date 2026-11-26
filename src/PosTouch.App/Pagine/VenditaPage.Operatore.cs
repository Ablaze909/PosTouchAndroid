using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    /// <summary>VenditaPage — operatore del turno (gestione operatori): tasto 👤, sospensione, chiusura turno, riepilogo.</summary>
    public partial class VenditaPage
    {
        private void AggiornaOperatore()
        {
            var op = ContestoCassa.Operatori;
            BtnOperatore.IsVisible = op.Abilitata;
            if (!op.Abilitata) return;
            BtnOperatore.Text = $"👤 {op.Corrente?.Nome ?? "—"}";
            // turno chiuso altrove, operatore disattivato o gestione appena accesa: senza turno non si vende
            if (!op.PuoLavorare)
                Dispatcher.Dispatch(async () => await Navigazione.TornaAllInizio(this));
        }

        private async void OnOperatore(object sender, EventArgs e)
        {
            if (Occupata || CombinazioneAperta()) return;
            var op = ContestoCassa.Operatori;
            if (!op.Abilitata || op.TurnoCorrente == null) return;
            var voci = new List<string> { "📊 RIEPILOGO TURNO", "⏸ SOSPENDI TURNO", "🔒 CHIUDI TURNO" };
            if (op.Corrente?.Amministratore == true) voci.Add("👥 GESTIONE OPERATORI");
            int k = await SceltaBox.Testi($"👤 {op.Corrente?.Nome} — turno dalle {op.TurnoCorrente.Apertura:HH:mm}", voci, 2);
            try
            {
                switch (k)
                {
                    case 0:
                        await MessaggioBox.Info("📊 RIEPILOGO TURNO", Turni.TestoRiepilogo(op.Riepilogo(op.TurnoCorrente)));
                        break;
                    case 1:
                        if (!await PuoLasciareAsync("sospendere")) return;
                        op.Sospendi();
                        await Navigazione.TornaAllInizio(this);
                        break;
                    case 2:
                        if (!await PuoLasciareAsync("chiudere")) return;
                        if (await Turni.ChiudiAsync(op.TurnoCorrente, op.Corrente))
                            await Navigazione.TornaAllInizio(this);
                        break;
                    case 3:
                        var admin = await Accesso.VerificaAmministratoreAsync("GESTIONE OPERATORI");
                        if (admin != null) await Navigazione.Apri(this, () => new OperatoriPage(admin));
                        break;
                }
            }
            catch (Exception ex)
            {
                PosTouch.Core.Diagnostica.LogManager.Error($"Menu operatore: {ex}");
                await MessaggioBox.Errore("TURNO", ex.Message);
            }
        }

        /// <summary>Il turno si lascia solo a scontrino vuoto e fuori dal tavolo (niente righe di nessuno a metà).</summary>
        private async Task<bool> PuoLasciareAsync(string azione)
        {
            if (_sessione != null) { await MessaggioBox.Avviso("TAVOLO APERTO", $"Esci dal tavolo prima di {azione} il turno."); return false; }
            if (!Carrello.Vuoto) { await MessaggioBox.Avviso("SCONTRINO IN CORSO", $"Chiudi o svuota lo scontrino prima di {azione} il turno."); return false; }
            return true;
        }
    }
}
