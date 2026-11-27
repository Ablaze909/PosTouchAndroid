namespace PosTouch.App.Servizi
{
    /// <summary>
    /// Altezza della tastiera a schermo di Android (in dp, 0 = chiusa), letta da MainActivity.
    /// Le pagine (<see cref="Controlli.PaginaPos"/>) lasciano libero questo spazio in basso, così il campo
    /// che si sta scrivendo e i tasti dei dialoghi restano visibili sopra la tastiera.
    /// </summary>
    public static class TastieraSchermo
    {
        public static double Altezza { get; private set; }

        /// <summary>Scatta sul thread dello schermo quando la tastiera si apre, si chiude o cambia altezza.</summary>
        public static event Action Cambiata;

        internal static void Imposta(double dp)
        {
            dp = dp < 40 ? 0 : Math.Round(dp);   // sotto i 40 dp non è una tastiera (barra dei suggerimenti, arrotondamenti)
            if (Math.Abs(dp - Altezza) < 1) return;
            Altezza = dp;
            try { Cambiata?.Invoke(); }
            catch (Exception ex) { PosTouch.Core.Diagnostica.LogManager.Error($"Tastiera a schermo: {ex}"); }
        }
    }
}
