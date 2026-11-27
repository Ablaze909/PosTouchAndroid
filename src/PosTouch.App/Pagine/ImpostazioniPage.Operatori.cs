using PosTouch.App.Dialoghi;
using PosTouch.App.Servizi;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Pagine
{
    /// <summary>
    /// ImpostazioniPage — 👥 OPERATORI E TURNI. Accendere/spegnere la gestione si salva subito (non con SALVA della pagina):
    /// per accenderla serve un amministratore (creato al momento se non c'è), per spegnerla il suo PIN.
    /// </summary>
    public partial class ImpostazioniPage
    {
        private bool _operatoriInCambio;

        private void CaricaOperatori()
        {
            _operatoriInCambio = true;
            ChkOperatori.IsChecked = ContestoCassa.Impostazioni.AbilitaGestioneOperatori;
            _operatoriInCambio = false;
            AggiornaOperatori();
        }

        private void AggiornaOperatori()
        {
            bool acceso = ContestoCassa.Impostazioni.AbilitaGestioneOperatori;
            OpzioniTurno.IsVisible = acceso;   // fondo e movimenti esistono solo con i turni
            CardPassword.Opacity = acceso ? 0.5 : 1;
            NotaOperatori.Text = acceso
                ? "Accesa: operatori e PIN si creano e modificano da BACK OFFICE → 👥 OPERATORI. Per vendere serve il PIN all'apertura del turno; Back office, Impostazioni, sconti, prezzi, annulli, storni e chiusura Z seguono i permessi dell'operatore. La password amministratore qui sopra serve solo come sblocco d'emergenza se non esiste nessun amministratore."
                : "Spenta: la cassa funziona senza operatori; la password amministratore protegge Strumenti, Back office e Impostazioni.";
        }

        private async void OnOperatoriCambiato(object sender, CheckedChangedEventArgs e)
        {
            if (_operatoriInCambio) return;
            _operatoriInCambio = true;
            bool ok = false;
            try { ok = e.Value ? await AccendiOperatoriAsync() : await SpegniOperatoriAsync(); }
            catch (Exception ex)
            {
                LogManager.Error($"Gestione operatori: {ex}");
                await MessaggioBox.Errore("OPERATORI", ex.Message);
            }
            finally
            {
                if (!ok) ChkOperatori.IsChecked = !e.Value;   // annullato o fallito: si torna com'era
                _operatoriInCambio = false;
                AggiornaOperatori();
            }
        }

        private async Task<bool> AccendiOperatoriAsync()
        {
            var archivio = ContestoCassa.Operatori.Archivio;
            if (!archivio.CeAmministratore())
            {
                await MessaggioBox.Info("👑 AMMINISTRATORE", "Per usare gli operatori serve prima un amministratore (di solito il titolare).\n\nScegli il suo nome e il suo PIN.");
                string nome = await TestoBox.Chiedi("NOME AMMINISTRATORE", "TITOLARE", 20);
                if (nome == null) return false;
                string pin = await ChiediPinNuovoAsync();
                if (pin == null) return false;
                var admin = new Operatore { Nome = nome, Amministratore = true };
                try { await Task.Run(() => archivio.Salva(admin, pin)); }
                catch (ArgumentException ex) { await MessaggioBox.Avviso("AMMINISTRATORE", ex.Message); return false; }
            }
            else
            {
                // amministratore già presente (accensione precedente): per riaccendere serve il suo PIN (o l'assistenza),
                // così non si accende per sbaglio una gestione di cui nessuno ricorda il PIN
                var chi = await Accesso.VerificaAmministratoreAsync("ACCENDI GESTIONE OPERATORI");
                if (chi == null) return false;
            }
            SalvaFlagOperatori(true);
            await MessaggioBox.Successo("OPERATORI", "Gestione operatori accesa.\n\nCrea gli altri operatori da BACK OFFICE → 👥 OPERATORI. Da ora per vendere serve il PIN.");
            return true;
        }

        private async Task<bool> SpegniOperatoriAsync()
        {
            var admin = await Accesso.VerificaAmministratoreAsync("SPEGNI GESTIONE OPERATORI");
            if (admin == null) return false;
            var turno = ContestoCassa.Operatori.TurnoAttivo();
            if (turno != null)
            {
                if (ContestoCassa.Operatori.TurnoCorrente?.Id == turno.Id && !ContestoCassa.Carrello.Vuoto)
                { await MessaggioBox.Avviso("SCONTRINO IN CORSO", "Chiudi o svuota lo scontrino prima di spegnere la gestione operatori."); return false; }
                if (!await MessaggioBox.Domanda("TURNO APERTO", $"Il turno di {turno.OperatoreNome} è ancora aperto: va chiuso prima di spegnere la gestione.\n\nChiuderlo adesso?")) return false;
                if (!await Turni.ChiudiAsync(turno, admin)) return false;
            }
            SalvaFlagOperatori(false);
            ContestoCassa.Operatori.Disattiva();
            return true;
        }

        private static void SalvaFlagOperatori(bool acceso)
        {
            var n = ContestoCassa.Impostazioni.Copia();
            n.AbilitaGestioneOperatori = acceso;
            ContestoCassa.SalvaImpostazioni(n);
            LogManager.Info($"Gestione operatori {(acceso ? "ACCESA" : "SPENTA")}");
        }

        private static async Task<string> ChiediPinNuovoAsync()
        {
            while (true)
            {
                string p1 = await TastierinoPrezzo.ChiediCodice("🔑 PIN (4-8 cifre)", nascondi: true);
                if (p1 == null) return null;
                try { PosTouch.Core.Dati.ArchivioOperatori.ControllaFormatoPin(p1); }
                catch (ArgumentException ex) { await MessaggioBox.Avviso("PIN", ex.Message); continue; }
                if (!Accesso.PinDisponibile(p1)) { await MessaggioBox.Avviso("PIN", "PIN non disponibile: scegline un altro."); continue; }
                string p2 = await TastierinoPrezzo.ChiediCodice("🔑 RIPETI IL PIN", nascondi: true);
                if (p2 == null) return null;
                if (p1 == p2) return p1;
                await MessaggioBox.Avviso("PIN", "I due PIN non coincidono, riprova.");
            }
        }
    }
}
