using PosTouch.App.Dialoghi;
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

        /// <summary>Strumenti, Back office, Impostazioni: password amministratore o permesso.</summary>
        public static Task<bool> VerificaPinAsync(string titolo, Permesso permesso = Permesso.Impostazioni)
            => OperatoriAccesi ? VerificaAsync(permesso, titolo) : PasswordAmministratoreAsync(titolo);

        /// <summary>Operazione protetta da permesso (con gestione operatori spenta è sempre consentita).</summary>
        public static async Task<bool> VerificaAsync(Permesso permesso, string titolo)
        {
            if (!OperatoriAccesi || ContestoCassa.Operatori.Puo(permesso)) return true;
            string pin = await TastierinoPrezzo.ChiediCodice($"🔑 {titolo}\nPIN di un operatore autorizzato", nascondi: true);
            if (pin == null) return false;
            if (ContestoCassa.Operatori.Autorizza(pin, permesso) != null) return true;
            await MessaggioBox.Errore("NON AUTORIZZATO", $"PIN errato o operatore senza il permesso:\n{Permessi.Nome(permesso)}");
            return false;
        }

        /// <summary>Gestione operatori: solo un amministratore (con il suo PIN, anche se è lui l'operatore del turno).</summary>
        public static async Task<Operatore> VerificaAmministratoreAsync(string titolo)
        {
            string pin = await TastierinoPrezzo.ChiediCodice($"🔑 {titolo}\nPIN amministratore", nascondi: true);
            if (pin == null) return null;
            var op = ContestoCassa.Operatori.AutorizzaAmministratore(pin);
            if (op == null) await MessaggioBox.Errore("NON AUTORIZZATO", "PIN errato o operatore non amministratore.");
            return op;
        }

        private static async Task<bool> PasswordAmministratoreAsync(string titolo)
        {
            string pin = ContestoCassa.Impostazioni.PinImpostazioni;
            if (string.IsNullOrEmpty(pin)) return true;

            string inserito = await TastierinoPrezzo.ChiediCodice($"🔑 {titolo}\nPassword amministratore", nascondi: true);
            if (inserito == null) return false;
            if (inserito == pin) return true;
            await MessaggioBox.Errore("PASSWORD ERRATA", "La password inserita non è corretta.");
            return false;
        }
    }
}
