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

        private static int _livelloMinimo;

        /// <summary>Livello minimo: "DEBUG" (tutto), "INFO", "WARN", "ERROR" (come Config.LivelloLog del PC).</summary>
        public static void SetLivelloMinimo(string livello) => _livelloMinimo = livello switch
        {
            "INFO" => 1,
            "WARN" => 2,
            "ERROR" => 3,
            _ => 0
        };

        public static void Debug(string msg) => Scrivi(0, "DEBUG", msg);
        public static void Info(string msg) => Scrivi(1, "INFO ", msg);
        public static void Warning(string msg) => Scrivi(2, "WARN ", msg);
        public static void Error(string msg) => Scrivi(3, "ERROR", msg);

        // ── scrittura in background (prestazioni) ──
        // Prima ogni riga apriva, scriveva e chiudeva il file sul thread che chiamava (spesso quello dello schermo).
        // Ora le righe vanno in coda e un thread a parte le scrive a blocchi: la cassa non aspetta mai il disco.
        private static readonly System.Collections.Concurrent.BlockingCollection<string> _coda =
            new System.Collections.Concurrent.BlockingCollection<string>(new System.Collections.Concurrent.ConcurrentQueue<string>(), 10000);
        private static System.Threading.Thread _scrittore;
        /// <summary>Righe accodate e non ancora scritte sul file (cala solo DOPO la scrittura).</summary>
        private static int _inSospeso;

        private static void Scrivi(int livelloNum, string livello, string msg)
        {
            if (_percorso == null || livelloNum < _livelloMinimo) return;
            string riga = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{livello}] {msg}{Environment.NewLine}";
            if (livelloNum >= 3)
            {
                // ERROR (rari e i più importanti): scritti subito, dopo quelli già in coda, così non si perdono se l'app si chiude
                Svuota();
                ScriviSubito(riga);
                return;
            }
            AvviaScrittore();
            System.Threading.Interlocked.Increment(ref _inSospeso);
            if (!_coda.TryAdd(riga)) { ScriviSubito(riga); System.Threading.Interlocked.Decrement(ref _inSospeso); }   // coda piena: scrive direttamente
        }

        private static void AvviaScrittore()
        {
            if (_scrittore != null) return;
            lock (_lock)
            {
                if (_scrittore != null) return;
                _scrittore = new System.Threading.Thread(CicloScrittura) { IsBackground = true, Name = "LogManager", Priority = System.Threading.ThreadPriority.BelowNormal };
                _scrittore.Start();
            }
        }

        private static void CicloScrittura()
        {
            var blocco = new System.Text.StringBuilder();
            foreach (var riga in _coda.GetConsumingEnumerable())
            {
                blocco.Clear().Append(riga);
                int righe = 1;
                while (blocco.Length < 64 * 1024 && _coda.TryTake(out var altra)) { blocco.Append(altra); righe++; }   // tutto ciò che è in coda in una sola scrittura
                ScriviSubito(blocco.ToString());
                System.Threading.Interlocked.Add(ref _inSospeso, -righe);
            }
        }

        /// <summary>Scrive sul file (con la rotazione a 5 MB). Mai eccezioni: il log non deve bloccare la cassa.</summary>
        private static void ScriviSubito(string testo)
        {
            lock (_lock)
            {
                try
                {
                    var fi = new FileInfo(_percorso);
                    if (fi.Exists && fi.Length > MaxDimensione)
                    {
                        // rotazione su 4 file (log, .1, .2, .3): gli errori fiscali di qualche giorno fa restano
                        if (File.Exists(_percorso + ".3")) File.Delete(_percorso + ".3");
                        for (int k = 2; k >= 1; k--)
                            if (File.Exists($"{_percorso}.{k}")) File.Move($"{_percorso}.{k}", $"{_percorso}.{k + 1}");
                        File.Move(_percorso, _percorso + ".1");
                    }
                    File.AppendAllText(_percorso, testo);
                }
                catch
                {
                    // Il log non deve mai bloccare la cassa
                }
            }
        }

        /// <summary>Attende (max ~1 s) che le righe in coda siano scritte: da chiamare prima di condividere il log.</summary>
        public static void Svuota()
        {
            for (int i = 0; i < 50 && System.Threading.Volatile.Read(ref _inSospeso) > 0; i++) System.Threading.Thread.Sleep(20);
        }
    }
}
