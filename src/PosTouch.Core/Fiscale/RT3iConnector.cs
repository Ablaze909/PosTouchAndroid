using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PosTouch.Core.Diagnostica;

namespace PosTouch.Core.Fiscale
{
    // ========== FIX #2, #6, #7: RT3iConnector riscritto ==========
    //
    // FIX #2: Dopo ogni comando, legge lo stream per intercettare XOFF (errore RT).
    //         Dopo la chiusura scontrino, legge il TX_FOOTER JSON dall'RT per ottenere
    //         il numero documento fiscale REALE emesso dal registratore.
    //
    // FIX #6: Implementa il controllo di flusso Xon/Xoff previsto dal protocollo 3i:
    //         - Legge XOFF (0x13) dal registratore = "buffer pieno, fermati"
    //         - Legge XON  (0x11) dal registratore = "puoi riprendere"
    //         - Il delay fisso di 150ms è sostituito da attesa attiva con timeout
    //
    // FIX #7: Implementa IDisposable per garantire chiusura connessione TCP
    //         anche in caso di eccezione. Usare con: using var rt = new RT3iConnector(...)
    //
    // NOTA SUL PROTOCOLLO 3i (da manuale v1.13):
    //   - I comandi base (R, T, M, O, =, K, @) NON prevedono ACK/NACK
    //   - L'RT invia XOFF (0x13) come segnale di errore o buffer pieno
    //   - L'RT invia XON (0x11) quando il buffer scende al 12%
    //   - I comandi F (89F, 88F, 84F, 50F) restituiscono risposte strutturate
    //   - TX_FOOTER ("3"901F = modalità JSON) restituisce conferma chiusura scontrino

    public partial class RT3iConnector : IFiscalConnector
    {
        private string _ip;
        private int _port;
        private TcpClient _client;
        private NetworkStream _stream;
        private bool _disposed = false;

        /// <summary>Ultimo errore ricevuto dall'RT (XOFF / timeout).</summary>
        public string UltimoErrore { get; private set; } = "";

        // Costanti protocollo Xon/Xoff
        private const byte XON  = 0x11;  // "Puoi trasmettere"
        private const byte XOFF = 0x13;  // "Fermati" / errore
        private const byte ACK  = 0x06;  // Comando accettato
        private const byte NAK  = 0x15;  // Comando rifiutato (errore hardware: coperchio/carta)
        private const byte CR   = 0x0D;  // Carriage Return (terminatore risposta)
        private const byte LF   = 0x0A;  // Line Feed (terminatore risposta)

        // Flag: se true, l'RT ha chiesto di fermarci (XOFF ricevuto)
        private volatile bool _xoffRicevuto = false;
        // Flag: se true, l'RT ha rifiutato un comando (NAK = errore hardware)
        private volatile bool _nakRicevuto = false;

        // Buffer per byte letti da ControllaXoffAsync che NON sono XON/XOFF.
        // Questi byte sono parte della risposta RT e devono essere riletti da LeggiRispostaAsync.
        // FIX BUG-7: volatile su campi scalari — se ControllaXoffAsync e LeggiRispostaAsync
        // venissero mai invocati concorrentemente (oggi sono sequenziali ma non garantito),
        // senza volatile il JIT potrebbe tenere i valori in registro causando data race.
        private byte[] _pendingBytes = null;
        private volatile int _pendingOffset = 0;
        private volatile int _pendingCount = 0;

        // Timeout per attesa risposte dall'RT
        // Timeout letti da Config al momento della costruzione (configurabili da Impostazioni → Avanzate)
        private readonly int TIMEOUT_RISPOSTA_MS;
        private readonly int TIMEOUT_TX_FOOTER_MS;
        private readonly int TIMEOUT_CONNESSIONE_MS;
        
        // Delay tra comandi (minimo per dare tempo all'RT di processare)
        private const int DELAY_POST_COMANDO_MS = 80;

        public RT3iConnector(string ip, int port = 1723)
        {
            _ip = ip;
            _port = port;
            // Leggi timeout da Config (con fallback ai valori storici se non configurati)
            var cfg = ImpostazioniFiscali.Correnti; // ANDROID: era Config.Carica()
            TIMEOUT_CONNESSIONE_MS = cfg.TimeoutConnessioneRT_ms > 0 ? cfg.TimeoutConnessioneRT_ms : 5000;
            TIMEOUT_RISPOSTA_MS    = cfg.TimeoutRispostaRT_ms    > 0 ? cfg.TimeoutRispostaRT_ms    : 10000;
            TIMEOUT_TX_FOOTER_MS   = TIMEOUT_RISPOSTA_MS + 5000; // TX_FOOTER sempre +5s rispetto alla risposta normale
        }

        // ========== CONNESSIONE ==========

        // FIX 2.2: Aggiunto CancellationToken per permettere cancellazione
        // Se l'utente chiude l'app o la rete è instabile, il thread non resta bloccato
        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                LogManager.Info($"\n=== CONNESSIONE {_ip}:{_port} ===");
                
                _client = new TcpClient();
                
                // FIX #7 + FIX 2.2: Timeout connessione con supporto cancellazione
                var connectTask = _client.ConnectAsync(_ip, _port);
                var timeoutTask = Task.Delay(TIMEOUT_CONNESSIONE_MS, ct);
                
                if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                {
                    _client?.Close();
                    // FIX BUG-2: osserva l'eccezione tardiva di connectTask per evitare
                    // UnobservedTaskException. Prima del fix, se ConnectAsync falliva dopo il timeout
                    // l'eccezione rimaneva non osservata e poteva crashare il processo.
                    _ = connectTask.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            LogManager.Debug($"RT3i: connectTask eccezione tardiva (attesa): {t.Exception?.GetBaseException().Message}");
                    }, TaskContinuationOptions.OnlyOnFaulted);
                    if (ct.IsCancellationRequested)
                        LogManager.Warning("Connessione CANCELLATA dall'utente");
                    else
                        LogManager.Warning("TIMEOUT connessione");
                    return false;
                }
                
                if (!_client.Connected)
                {
                    LogManager.Error("Connessione FALLITA");
                    return false;
                }
                
                _stream = _client.GetStream();
                _stream.ReadTimeout = 5000;
                _stream.WriteTimeout = 5000;
                _xoffRicevuto = false;
                _nakRicevuto = false;
                
                // Svuota eventuali dati residui nello stream (es. XON di accensione)
                await SvuotaStreamAsync();
                
                LogManager.Info("CONNESSO OK");
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Error($"ERRORE connessione: {ex.Message}");
                return false;
            }
        }

        // ========== DISCONNESSIONE E DISPOSE (FIX #7) ==========

        /// <summary>
        /// Converte data 3i AAMMGG (YYMMDD) → "yyyy-MM-dd" normalizzato.
        /// </summary>
        private static string NormalizzaData3i(string aammgg)
        {
            if (string.IsNullOrEmpty(aammgg) || aammgg.Length < 6) return null;
            try
            {
                int yy = int.Parse(aammgg.Substring(0, 2));
                int mm = int.Parse(aammgg.Substring(2, 2));
                int dd = int.Parse(aammgg.Substring(4, 2));
                // FIX BUG-11: guard esplicita — yy > 99 (es. yy=200 da parsing errato) causa
                // ArgumentOutOfRangeException in new DateTime(2200, ...). Logga e restituisce null
                // invece di lasciare che il try/catch esterno perda il contesto diagnostico.
                if (yy < 0 || yy > 99)
                {
                    LogManager.Warning($"NormalizzaData3i: anno fuori range ({yy}), raw='{aammgg}'");
                    return null;
                }
                // FIX BUG-M6: Sanity check — l'RT non dovrebbe avere date oltre 10 anni nel futuro
                // (prima anno 99 → 2099, ora logga warning per date sospette)
                var data = new DateTime(2000 + yy, mm, dd);
                if (data.Year > DateTime.Now.Year + 10)
                    LogManager.Warning($"NormalizzaData3i: data sospetta {data:yyyy-MM-dd} (raw='{aammgg}')");
                return data.ToString("yyyy-MM-dd");
            }
            catch (Exception ex) { LogManager.Debug($"NormalizzaData3i parse fallito: {ex.Message}"); return null; }
        }

        public void Disconnect()
        {
            try
            {
                _stream?.Close();
                _client?.Close();
                LogManager.Info("=== DISCONNESSO ===");
            }
            catch (Exception ex) { LogManager.Debug($"Errore disconnessione: {ex.Message}"); }
        }

        // ========== DISPOSE ==========

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try { _stream?.Dispose(); } catch (Exception ex) { LogManager.Debug($"Errore dispose stream: {ex.Message}"); }
                    try { _client?.Dispose(); } catch (Exception ex) { LogManager.Debug($"Errore dispose client: {ex.Message}"); }
                }
                _disposed = true;
            }
        }

        ~RT3iConnector()
        {
            Dispose(false);
        }
    }
}
