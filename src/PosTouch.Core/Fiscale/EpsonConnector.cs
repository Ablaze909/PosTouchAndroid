using System;
using PosTouch.Core.Diagnostica;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PosTouch.Core.Fiscale
{
    // ========================================================================
    // EpsonConnector — Protocollo Epson Communication Protocol (Rev 6.00)
    //
    // Frame PDU: STX + CNT(2) + "E" + H1(1) + H2(3) + DATA + CKS(2) + ETX
    //
    // Risposte normali: stessa struttura con dati di ritorno
    // Risposte errore:  A.PDU = "ERR" + OP(2) + N(2)   (codice errore)
    //
    // Se il frame inviato ha errori di checksum, la stampante NON risponde.
    //
    // Porta TCP default: 9100
    // ========================================================================

    public partial class EpsonConnector : IFiscalConnector
    {
        private readonly string _ip;
        private readonly int _port;
        private TcpClient _client;
        private NetworkStream _stream;
        private bool _disposed;
        private int _counter;
        private int _op = 1; // Operatore corrente (1-12)

        private RisultatoChiusuraRT _ultimaChiusura;

        /// <summary>Ultimo errore Epson (codice + descrizione) per messaggi UI.</summary>
        public string UltimoErrore { get; private set; } = "";

        private const byte STX = 0x02;
        private const byte ETX = 0x03;
        // Timeout configurabili da Config (con fallback ai valori storici)
        private readonly int TIMEOUT_CONNECT;
        private readonly int TIMEOUT_CMD;
        private readonly int TIMEOUT_PRINT;

        public EpsonConnector(string ip, int port = 9100)
        {
            _ip = ip;
            _port = port;
            var cfg = ImpostazioniFiscali.Correnti; // ANDROID: era Config.Carica()
            TIMEOUT_CONNECT = cfg.TimeoutConnessioneRT_ms > 0 ? cfg.TimeoutConnessioneRT_ms : 5000;
            TIMEOUT_CMD     = cfg.TimeoutRispostaRT_ms    > 0 ? cfg.TimeoutRispostaRT_ms    : 10000;
            TIMEOUT_PRINT   = Math.Max(TIMEOUT_CMD, 10000) + 20000; // stampa sempre +20s rispetto al CMD
        }

        // ====================== CONNESSIONE ======================

        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                LogManager.Info($"\n{'=',0}{'=',0}{'=',0} CONNESSIONE EPSON {_ip}:{_port} {'=',0}{'=',0}{'=',0}");
                _client = new TcpClient();

                var task = _client.ConnectAsync(_ip, _port);
                if (await Task.WhenAny(task, Task.Delay(TIMEOUT_CONNECT, ct)) != task)
                {
                    _client.Close();
                    // FIX BUG-2: osserva l'eccezione tardiva per evitare UnobservedTaskException
                    _ = task.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            LogManager.Debug($"Epson: connectTask eccezione tardiva (attesa): {t.Exception?.GetBaseException().Message}");
                    }, TaskContinuationOptions.OnlyOnFaulted);
                    LogManager.Warning("Epson TIMEOUT connessione TCP");
                    return false;
                }

                if (!_client.Connected) return false;

                _stream = _client.GetStream();
                _stream.ReadTimeout = 5000;
                _stream.WriteTimeout = 5000;
                _counter = 0;

                // Svuota buffer
                await Task.Delay(50);
                if (_stream.DataAvailable)
                {
                    var trash = new byte[4096];
                    await _stream.ReadAsync(trash, 0, trash.Length);
                }

                // Sincronizza counter: 2 GET STATUS rapidi (raccomandato manuale pag 24)
                // Timeout breve: se stampante non risponde, continuiamo comunque
                LogManager.Debug("Epson: sync counter...");
                await SendAsync(1, 74, Op2, 1500);
                await Task.Delay(30);
                var r2 = await SendAsync(1, 74, Op2, 1500);

                if (r2.Success)
                    LogManager.Info($"=== EPSON CONNESSO OK — status=\"{r2.Data}\" ===");
                else
                    LogManager.Warning($"Epson connesso, status: {(r2.IsError ? r2.ErrorMessage : "timeout")}");

                return true; // TCP connesso — anche senza risposta status, proviamo i comandi
            }
            catch (Exception ex)
            {
                LogManager.Error($"Epson ERRORE connessione: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            try { _stream?.Close(); } catch (Exception ex) { LogManager.Debug($"Stream close: {ex.Message}"); }
            try { _client?.Close(); } catch (Exception ex) { LogManager.Debug($"Client close: {ex.Message}"); }
            LogManager.Info("=== EPSON DISCONNESSO ===");
        }

        // ====================== DISPOSE ======================

        // FIX BUG-8: Dispose pattern completo — allineato a RT3iConnector.
        // Il finalizer chiamava Dispose() pubblico invece di Dispose(false), violando il pattern standard.
        // Ora: public Dispose() → Dispose(true); finalizer → Dispose(false).
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing)
                {
                    // Risorse managed: dispose solo quando chiamato esplicitamente (non dal finalizer)
                    try { _stream?.Dispose(); } catch (Exception ex) { LogManager.Debug($"Stream dispose: {ex.Message}"); }
                    try { _client?.Dispose(); } catch (Exception ex) { LogManager.Debug($"Client dispose: {ex.Message}"); }
                }
                // Risorse unmanaged andrebbero qui (nessuna al momento)
            }
        }

        ~EpsonConnector() { Dispose(false); }
    }
}
