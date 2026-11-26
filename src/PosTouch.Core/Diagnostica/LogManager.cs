using System;
using System.IO;

namespace PosTouch.Core.Diagnostica
{
    /// <summary>
    /// Log su file (debug.log) con rotazione a 5 MB.
    /// Stessa interfaccia della versione Windows (Debug/Info/Warning/Error),
    /// così i connettori fiscali restano identici.
    /// L'app chiama <see cref="Inizializza"/> all'avvio con la propria cartella dati.
    /// </summary>
    public static class LogManager
    {
        private static readonly object _lock = new object();
        private static string _percorso;
        private const long MaxDimensione = 5 * 1024 * 1024;

        public static string Percorso => _percorso;

        public static void Inizializza(string cartella)
        {
            Directory.CreateDirectory(cartella);
            _percorso = Path.Combine(cartella, "debug.log");
        }

        public static void Debug(string msg) => Scrivi("DEBUG", msg);
        public static void Info(string msg) => Scrivi("INFO ", msg);
        public static void Warning(string msg) => Scrivi("WARN ", msg);
        public static void Error(string msg) => Scrivi("ERROR", msg);

        private static void Scrivi(string livello, string msg)
        {
            if (_percorso == null) return;
            lock (_lock)
            {
                try
                {
                    var fi = new FileInfo(_percorso);
                    if (fi.Exists && fi.Length > MaxDimensione)
                    {
                        string vecchio = _percorso + ".1";
                        if (File.Exists(vecchio)) File.Delete(vecchio);
                        File.Move(_percorso, vecchio);
                    }
                    File.AppendAllText(_percorso, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{livello}] {msg}{Environment.NewLine}");
                }
                catch
                {
                    // Il log non deve mai bloccare la cassa
                }
            }
        }
    }
}
