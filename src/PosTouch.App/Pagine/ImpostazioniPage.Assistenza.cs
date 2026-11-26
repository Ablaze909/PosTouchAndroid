using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Diagnostica;

namespace PosTouch.App.Pagine
{
    /// <summary>🔧 Password assistenza tecnica: salvata subito (solo hash), cambiabile solo da chi conosce quella attuale.</summary>
    public partial class ImpostazioniPage
    {
        private void AggiornaAssistenza()
            => TestoAssistenza.Text = PasswordAssistenza.Impostata(ContestoCassa.Impostazioni)
                ? "●●●●●● (clicca per cambiare o togliere)" : "Non configurata (clicca per impostare)";

        private bool _assistenzaInCorso;

        private async void OnPasswordAssistenza(object sender, TappedEventArgs e)
        {
            if (_assistenzaInCorso) return;
            _assistenzaInCorso = true;
            try { await CambiaAssistenzaAsync(); }
            catch (Exception ex)
            {
                LogManager.Error($"Password assistenza: {ex}");
                await MessaggioBox.Errore("PASSWORD ASSISTENZA", ex.Message);
            }
            finally { _assistenzaInCorso = false; AggiornaAssistenza(); }
        }

        private static async Task CambiaAssistenzaAsync()
        {
            var cfg = ContestoCassa.Impostazioni;
            if (PasswordAssistenza.Impostata(cfg))
            {
                string attuale = await TastierinoPrezzo.ChiediCodice("🔧 PASSWORD ASSISTENZA ATTUALE", nascondi: true);
                if (attuale == null) return;
                if (!await Task.Run(() => PasswordAssistenza.Verifica(cfg, attuale)))
                {
                    await MessaggioBox.Errore("PASSWORD ERRATA", "La password assistenza attuale non è corretta.");
                    return;
                }
            }
            string nuova = await TastierinoPrezzo.ChiediCodice($"🔧 NUOVA PASSWORD ASSISTENZA\n(almeno {PasswordAssistenza.LunghezzaMinima} cifre — vuota = togli)", nascondi: true);
            if (nuova == null) return;
            if (nuova.Length > 0)
            {
                if (nuova.Length < PasswordAssistenza.LunghezzaMinima)
                {
                    await MessaggioBox.Avviso("PASSWORD ASSISTENZA", $"Servono almeno {PasswordAssistenza.LunghezzaMinima} cifre.");
                    return;
                }
                string conferma = await TastierinoPrezzo.ChiediCodice("🔧 RIPETI LA PASSWORD ASSISTENZA", nascondi: true);
                if (conferma == null) return;
                if (conferma != nuova) { await MessaggioBox.Errore("PASSWORD", "Le due password non coincidono."); return; }
            }
            else if (!await MessaggioBox.Conferma("PASSWORD ASSISTENZA", "Togliere la password assistenza?")) return;

            var n = ContestoCassa.Impostazioni.Copia();
            await Task.Run(() => PasswordAssistenza.Imposta(n, nuova));
            ContestoCassa.SalvaImpostazioni(n);
            LogManager.Info(nuova.Length > 0 ? "Password assistenza impostata" : "Password assistenza tolta");
            await MessaggioBox.Successo("PASSWORD ASSISTENZA", nuova.Length > 0 ? "Password assistenza salvata." : "Password assistenza tolta.");
        }
    }
}
