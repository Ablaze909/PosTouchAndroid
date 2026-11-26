using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PosTouch.Core.Stampa
{
    /// <summary>Una stampa alla volta per ogni stampante (IP:porta): conti, comande e prove non si accavallano.</summary>
    public static class AccessoStampanti
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _blocchi = new ConcurrentDictionary<string, SemaphoreSlim>();

        private static SemaphoreSlim Blocco(string ip, int porta) =>
            _blocchi.GetOrAdd($"{ip}:{porta}", _ => new SemaphoreSlim(1, 1));

        public static Task<bool> PrendiAsync(string ip, int porta, TimeSpan attesa) => Blocco(ip, porta).WaitAsync(attesa);

        public static void Rilascia(string ip, int porta) => Blocco(ip, porta).Release();

        /// <summary>Collega, invia e chiude, con il blocco della stampante. Restituisce null se ok, altrimenti l'errore.</summary>
        public static async Task<string> InviaAsync(string ip, int porta, byte[] dati, TimeSpan attesa)
        {
            if (!await PrendiAsync(ip, porta, attesa)) return "Stampante occupata da un'altra stampa.";
            try
            {
                using var st = new StampanteTermica(ip, porta);
                if (!await st.ConnettiAsync()) return st.UltimoErrore;
                await st.InviaAsync(dati);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                Rilascia(ip, porta);
            }
        }
    }
}
