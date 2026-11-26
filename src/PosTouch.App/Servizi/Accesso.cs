using PosTouch.App.Dialoghi;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Servizi
{
    /// <summary>
    /// Chi può fare cosa.
    /// Gestione operatori SPENTA: password amministratore (solo cifre) per Strumenti, Back office e Impostazioni; il resto è libero.
    /// Gestione operatori ACCESA: permessi dell'operatore del turno; se manca, chi inserisce il suo PIN si identifica e,
    /// se ha il permesso, autorizza solo quell'operazione (come il PC).
    /// </summary>
    public static class Accesso
    {
        private static bool OperatoriAccesi => ContestoCassa.Operatori?.Abilitata == true;

        /// <summary>Operatore "di comodo" con cui l'assistenza tecnica fa le operazioni da amministratore.</summary>
        public static Operatore Assistenza => new Operatore { Id = 0, Nome = "ASSISTENZA", Amministratore = true };

        /// <summary>Un PIN operatore non può coincidere con la password assistenza (darebbe a quell'operatore accesso a tutto).</summary>
        public static bool PinDisponibile(string pin) => !PasswordAssistenza.Verifica(ContestoCassa.Impostazioni, pin);

        /// <summary>true = il codice inserito è la password assistenza (apre tutto; lasciata traccia nel log).</summary>
        internal static bool EAssistenza(string codice, string titolo)
        {
            if (!PasswordAssistenza.Verifica(ContestoCassa.Impostazioni, codice)) return false;
            LogManager.Warning($"🔧 ACCESSO ASSISTENZA: {titolo?.Replace('\n', ' ')}");
            return true;
        }

        /// <summary>
        /// Gestione accesa ma nessun amministratore attivo (operatori.db perso o rovinato): si ripiega sulla password
        /// amministratore, così la cassa si può sempre sbloccare da Impostazioni.
        /// </summary>
        private static bool SenzaAmministratore
        {
            get
            {
                try { return !ContestoCassa.Operatori.Archivio.CeAmministratore(); }
                catch (Exception ex) { LogManager.Error($"Lettura operatori: {ex.Message}"); return true; }
            }
        }

        /// <summary>Strumenti, Back office, Impostazioni: password amministratore o permesso.</summary>
        public static Task<bool> VerificaPinAsync(string titolo, Permesso permesso = Permesso.Impostazioni)
            => OperatoriAccesi ? VerificaAsync(permesso, titolo) : PasswordAmministratoreAsync(titolo);

        /// <summary>Operazione protetta da permesso (con gestione operatori spenta è sempre consentita).</summary>
        public static async Task<bool> VerificaAsync(Permesso permesso, string titolo)
        {
            if (!OperatoriAccesi || ContestoCassa.Operatori.Puo(permesso)) return true;
            if (SenzaAmministratore) return await PasswordAmministratoreAsync(titolo);
            string pin = await TastierinoPrezzo.ChiediCodice($"🔑 {titolo}\nPIN di un operatore autorizzato", nascondi: true);
            if (pin == null) return false;
            if (await Task.Run(() => EAssistenza(pin, titolo))) return true;
            var chi = await Task.Run(() => ContestoCassa.Operatori.Archivio.TrovaPerPin(pin));   // hash del PIN fuori dal thread grafico
            if (chi?.Puo(permesso) == true)
            {
                LogManager.Info($"AUTORIZZAZIONE {permesso}: {chi.Nome} per {ContestoCassa.Operatori.Corrente?.Nome ?? "nessun operatore"}");
                return true;
            }
            await MessaggioBox.Errore("NON AUTORIZZATO", $"PIN errato o operatore senza il permesso:\n{Permessi.Nome(permesso)}");
            return false;
        }

        /// <summary>Gestione operatori: solo un amministratore (con il suo PIN, anche se è lui l'operatore del turno).</summary>
        public static async Task<Operatore> VerificaAmministratoreAsync(string titolo)
        {
            // emergenza: nessun amministratore attivo → password amministratore (o libero se non c'è), per non bloccare la cassa
            if (SenzaAmministratore)
            {
                if (!await PasswordAmministratoreAsync(titolo)) return null;
                LogManager.Warning($"{titolo}: nessun amministratore attivo, accesso di emergenza con la password amministratore");
                return new Operatore { Id = 0, Nome = "EMERGENZA", Amministratore = true };
            }
            string pin = await TastierinoPrezzo.ChiediCodice($"🔑 {titolo}\nPIN amministratore", nascondi: true);
            if (pin == null) return null;
            if (await Task.Run(() => EAssistenza(pin, titolo))) return Assistenza;
            var op = await Task.Run(() => ContestoCassa.Operatori.Archivio.TrovaPerPin(pin));
            if (op?.Amministratore == true) return op;
            await MessaggioBox.Errore("NON AUTORIZZATO", "PIN errato o operatore non amministratore.");
            return null;
        }

        private static async Task<bool> PasswordAmministratoreAsync(string titolo)
        {
            string pin = ContestoCassa.Impostazioni.PinImpostazioni;
            if (string.IsNullOrEmpty(pin)) return true;

            string inserito = await TastierinoPrezzo.ChiediCodice($"🔑 {titolo}\nPassword amministratore", nascondi: true);
            if (inserito == null) return false;
            if (inserito == pin) return true;
            if (await Task.Run(() => EAssistenza(inserito, titolo))) return true;
            await MessaggioBox.Errore("PASSWORD ERRATA", "La password inserita non è corretta.");
            return false;
        }
    }
}
