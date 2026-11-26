namespace PosTouch.App.Servizi
{
    /// <summary>Protezione con PIN delle pagine di gestione (Impostazioni, Articoli).</summary>
    public static class Accesso
    {
        public static async Task<bool> VerificaPinAsync(Page pagina, string titolo)
        {
            string pin = ContestoCassa.Impostazioni.PinImpostazioni;
            if (string.IsNullOrEmpty(pin)) return true;

            string inserito = await pagina.DisplayPromptAsync(titolo, "Inserire il PIN", "OK", "Annulla", keyboard: Keyboard.Numeric);
            if (inserito == null) return false;
            if (inserito == pin) return true;
            await pagina.DisplayAlert(titolo, "PIN errato.", "OK");
            return false;
        }
    }
}
