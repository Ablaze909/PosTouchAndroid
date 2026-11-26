using System;
using System.Threading;
using System.Threading.Tasks;

namespace PosTouch.Core.Fiscale
{
    /// <summary>
    /// Un solo dialogo alla volta con il registratore: vendita, cassetto, letture e chiusure
    /// non aprono mai due connessioni contemporanee (l'RT ne accetta una sola).
    /// </summary>
    public static class AccessoRT
    {
        private static readonly SemaphoreSlim _blocco = new SemaphoreSlim(1, 1);

        public static Task<bool> PrendiAsync(TimeSpan attesa) => _blocco.WaitAsync(attesa);

        public static void Rilascia() => _blocco.Release();

        public const string MessaggioOccupato =
            "Il registratore sta eseguendo un'altra operazione.\nAttendere qualche secondo e riprovare.";
    }
}
