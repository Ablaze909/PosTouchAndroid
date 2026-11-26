using PosTouch.App.Dialoghi;
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

        // ── tentativi PIN: dopo 5 errori di fila si aspetta (30 s, poi di più) ──
        private static int _errori;
        private static DateTime _bloccoFino = DateTime.MinValue;

        /// <summary>false = troppi PIN errati, bisogna aspettare (il messaggio lo mostra già).</summary>
        internal static async Task<bool> TentativoConsentitoAsync()
        {
            var attesa = _bloccoFino - DateTime.Now;
            if (attesa <= TimeSpan.Zero) return true;
            await MessaggioBox.Avviso("TROPPI TENTATIVI", $"Troppi PIN errati.\n\nRiprova tra {Math.Ceiling(attesa.TotalSeconds)} secondi.");
            return false;
        }

        internal static void EsitoPin(bool corretto)
        {
            if (corretto) { _errori = 0; return; }
            _errori++;
            if (_errori >= 5)
            {
                _bloccoFino = DateTime.Now.AddSeconds(30 * Math.Min(8, _errori - 4));
                LogManager.Warning($"PIN errato {_errori} volte di fila: accesso bloccato fino alle {_bloccoFino:HH:mm:ss}");
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
            if (!await TentativoConsentitoAsync()) return false;
            string pin = await TastierinoPrezzo.ChiediCodice($"🔑 {titolo}\nPIN di un operatore autorizzato", nascondi: true);
            if (pin == null) return false;
            var chi = await Task.Run(() => ContestoCassa.Operatori.Archivio.TrovaPerPin(pin));   // hash del PIN fuori dal thread grafico
            EsitoPin(chi != null);
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
            if (!await TentativoConsentitoAsync()) return null;
            string pin = await TastierinoPrezzo.ChiediCodice($"🔑 {titolo}\nPIN amministratore", nascondi: true);
            if (pin == null) return null;
            var op = await Task.Run(() => ContestoCassa.Operatori.Archivio.TrovaPerPin(pin));
            EsitoPin(op != null);
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
            await MessaggioBox.Errore("PASSWORD ERRATA", "La password inserita non è corretta.");
            return false;
        }
    }
}
