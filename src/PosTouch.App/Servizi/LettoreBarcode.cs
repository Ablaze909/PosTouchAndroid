using System.Text;

namespace PosTouch.App.Servizi
{
    /// <summary>
    /// Lettore barcode USB/Bluetooth (si presenta come una tastiera).
    /// I tasti arrivano da MainActivity: le cifre/lettere vengono accumulate e con INVIO
    /// il codice viene passato alla pagina attiva. Così non serve un campo di testo con il focus
    /// (che farebbe comparire la tastiera a schermo).
    /// </summary>
    public static class LettoreBarcode
    {
        private static readonly StringBuilder _buffer = new StringBuilder();
        private static DateTime _ultimoTasto = DateTime.MinValue;

        /// <summary>Gestore attivo (la pagina vendita lo imposta quando è visibile). null = tasti non intercettati.</summary>
        public static Action<string> Destinatario { get; set; }

        public static bool Attivo => Destinatario != null;

        /// <summary>Caratteri di un barcode: lettere, cifre e simboli stampabili (CODE39/128: - . / + $ % …), niente spazi.</summary>
        public static bool Valido(char c) => char.IsLetterOrDigit(c) || (c > ' ' && c < (char)0x7F);

        /// <summary>Restituisce true se il tasto è stato usato dal lettore.</summary>
        public static bool Tasto(char c, bool invio)
        {
            if (!Attivo) return false;

            // Un lettore "scrive" velocissimo: una pausa lunga vuol dire nuovo codice
            if ((DateTime.Now - _ultimoTasto).TotalMilliseconds > 500) _buffer.Clear();
            _ultimoTasto = DateTime.Now;

            if (invio)
            {
                string codice = _buffer.ToString().Trim();
                _buffer.Clear();
                if (codice.Length > 0) MainThread.BeginInvokeOnMainThread(() => Destinatario?.Invoke(codice));
                return true;
            }
            if (Valido(c))
            {
                _buffer.Append(c);
                return true;
            }
            return false;
        }
    }
}
