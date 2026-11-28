using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PosTouch.Core.Diagnostica
{
    /// <summary>
    /// Errori di rete in italiano. Android e .NET danno i messaggi in inglese ("Network is unreachable",
    /// "Connection refused"…): al cassiere va detto cosa succede e cosa controllare.
    /// </summary>
    public static class ErroriRete
    {
        /// <summary>Testo dell'errore: per i problemi di rete una frase in italiano, altrimenti il messaggio originale.</summary>
        public static string Testo(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is SocketException se) return DaSocket(se.SocketErrorCode) ?? Traduci(se.Message);
                if (e is TimeoutException || e is TaskCanceledException || e is OperationCanceledException)
                    return "L'apparecchio non ha risposto in tempo (spento o rete lenta).";
            }
            string msg = ex?.Message ?? "";
            return PrimaFrase(msg) ?? msg;
        }

        private static string DaSocket(SocketError codice) => codice switch
        {
            SocketError.NetworkUnreachable or SocketError.NetworkDown =>
                "Rete non disponibile: il tablet non è collegato (controlla Wi-Fi o cavo).",
            SocketError.HostUnreachable or SocketError.HostDown or SocketError.HostNotFound =>
                "Apparecchio non raggiungibile: spento, scollegato o indirizzo IP sbagliato.",
            SocketError.ConnectionRefused =>
                "L'apparecchio ha rifiutato il collegamento (acceso ma non pronto, o porta sbagliata).",
            SocketError.TimedOut =>
                "L'apparecchio non ha risposto in tempo (spento o rete lenta).",
            SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Shutdown or SocketError.NotConnected =>
                "Collegamento interrotto con l'apparecchio.",
            SocketError.AddressNotAvailable =>
                "Indirizzo IP non valido per questa rete.",
            _ => null
        };

        /// <summary>Messaggio che è (o contiene) un errore di rete noto: la frase italiana, una sola.</summary>
        private static string PrimaFrase(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return null;
            foreach (var (en, it) in Frasi)
                if (msg.IndexOf(en, StringComparison.OrdinalIgnoreCase) >= 0) return it.TrimEnd('.') + ".";
            return null;
        }

        private static readonly (string en, string it)[] Frasi =
            {
                ("Network is unreachable", "Rete non disponibile: il tablet non è collegato (controlla Wi-Fi o cavo)"),
                ("Network unreachable", "Rete non disponibile: il tablet non è collegato (controlla Wi-Fi o cavo)"),
                ("No route to host", "Apparecchio non raggiungibile: spento, scollegato o indirizzo IP sbagliato"),
                ("Host is unreachable", "Apparecchio non raggiungibile: spento, scollegato o indirizzo IP sbagliato"),
                ("Connection refused", "L'apparecchio ha rifiutato il collegamento (acceso ma non pronto, o porta sbagliata)"),
                ("Connection reset by peer", "Collegamento interrotto con l'apparecchio"),
                ("Broken pipe", "Collegamento interrotto con l'apparecchio"),
                ("Connection timed out", "L'apparecchio non ha risposto in tempo (spento o rete lenta)"),
                ("Operation timed out", "L'apparecchio non ha risposto in tempo (spento o rete lenta)"),
                ("A task was canceled.", "L'apparecchio non ha risposto in tempo (spento o rete lenta)."),
                ("The operation was canceled.", "L'apparecchio non ha risposto in tempo (spento o rete lenta)."),
                ("Software caused connection abort", "Collegamento interrotto con l'apparecchio"),
                ("Name or service not known", "Indirizzo dell'apparecchio non trovato"),
                ("Unable to read data from the transport connection", "Collegamento interrotto con l'apparecchio"),
                ("Unable to write data to the transport connection", "Collegamento interrotto con l'apparecchio"),
                ("Network subsystem is down", "Rete non disponibile: il tablet non è collegato (controlla Wi-Fi o cavo)"),
            };

        /// <summary>Sostituisce i messaggi inglesi più comuni (anche dentro testi più lunghi).</summary>
        public static string Traduci(string testo)
        {
            if (string.IsNullOrEmpty(testo)) return testo;
            string t = testo;
            foreach (var (en, it) in Frasi)
            {
                int i = t.IndexOf(en, StringComparison.OrdinalIgnoreCase);
                while (i >= 0)
                {
                    t = t.Substring(0, i) + it + t.Substring(i + en.Length);
                    i = t.IndexOf(en, i + it.Length, StringComparison.OrdinalIgnoreCase);
                }
            }
            // "Collegamento interrotto…: Collegamento interrotto…" (messaggio inglese a due livelli) → una volta sola
            foreach (var (_, it) in Frasi)
                t = t.Replace(it + ": " + it, it);
            return t;
        }
    }
}
