namespace PosTouch.Core.Fiscale
{
    /// <summary>
    /// Timeout letti dai connettori nel costruttore.
    /// Sostituisce la chiamata Config.Carica() della versione Windows:
    /// l'app imposta <see cref="Correnti"/> all'avvio e a ogni salvataggio impostazioni.
    /// </summary>
    public class ImpostazioniFiscali
    {
        public int TimeoutConnessioneRT_ms { get; set; } = 5000;
        public int TimeoutRispostaRT_ms { get; set; } = 10000;

        public static ImpostazioniFiscali Correnti { get; set; } = new ImpostazioniFiscali();
    }
}
