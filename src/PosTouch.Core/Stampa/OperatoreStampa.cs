using System;

namespace PosTouch.Core.Stampa
{
    /// <summary>
    /// Nome dell'operatore del turno da stampare su comande e conti (gestione operatori).
    /// Impostato dalla cassa; vuoto con la gestione spenta. Letto una volta quando la stampa viene preparata.
    /// </summary>
    public static class OperatoreStampa
    {
        public static Func<string> Nome { get; set; }

        public static string Corrente()
        {
            try { return (Nome?.Invoke() ?? "").Trim(); }
            catch { return ""; }   // la stampa non deve mai fallire per il nome dell'operatore
        }
    }
}
