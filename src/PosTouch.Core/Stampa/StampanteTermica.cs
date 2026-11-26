using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PosTouch.Core.Diagnostica;

namespace PosTouch.Core.Stampa
{
    /// <summary>Collegamento TCP alla stampante termica in rete (porta 9100), come sul PC.</summary>
    public class StampanteTermica : IDisposable
    {
        private const int TimeoutConnessioneMs = 1500;
        private const int TimeoutInvioMs = 5000;

        private readonly string _ip;
        private readonly int _porta;
        private TcpClient _client;
        private NetworkStream _stream;

        public string UltimoErrore { get; private set; }

        public StampanteTermica(string ip, int porta)
        {
            _ip = ip;
            _porta = porta;
        }

        public async Task<bool> ConnettiAsync()
        {
            try
            {
                _client = new TcpClient { SendTimeout = TimeoutInvioMs };
                using var cts = new CancellationTokenSource(TimeoutConnessioneMs);
                await _client.ConnectAsync(_ip, _porta, cts.Token);
                _stream = _client.GetStream();
                return true;
            }
            catch (OperationCanceledException)
            {
                UltimoErrore = $"Stampante non risponde ({_ip}:{_porta}).";
            }
            catch (Exception ex)
            {
                UltimoErrore = $"Stampante non raggiungibile ({_ip}:{_porta}): {ex.Message}";
            }
            LogManager.Warning($"Termica: {UltimoErrore}");
            Chiudi();
            return false;
        }

        public async Task InviaAsync(byte[] dati)
        {
            using var cts = new CancellationTokenSource(TimeoutInvioMs);
            await _stream.WriteAsync(dati, 0, dati.Length, cts.Token);
            await _stream.FlushAsync(cts.Token);
        }

        private void Chiudi()
        {
            try { _stream?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }
            _stream = null;
            _client = null;
        }

        public void Dispose() => Chiudi();
    }
}
