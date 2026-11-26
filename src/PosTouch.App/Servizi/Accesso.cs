using PosTouch.App.Dialoghi;

namespace PosTouch.App.Servizi
{
    /// <summary>Password amministratore (solo cifre) per Strumenti, Back office e Impostazioni.</summary>
    public static class Accesso
    {
        public static async Task<bool> VerificaPinAsync(string titolo)
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
