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

        /// <summary>
        /// Controllo di stato (DLE EOT): chiede alla stampante carta e coperchio prima di stampare.
        /// Restituisce null se pronta, altrimenti il motivo. Se la stampante non risponde allo stato si stampa lo stesso.
        /// </summary>
        public async Task<string> ControllaStatoAsync()
        {
            try
            {
                // n=2: stato offline (bit 2 = coperchio aperto, bit 5 = carta finita che ferma la stampa)
                var off = await ChiediStatoAsync(2);
                if (off.HasValue && (off.Value & 0x04) != 0) return "Coperchio della stampante aperto.";
                if (off.HasValue && (off.Value & 0x20) != 0) return "Carta finita.";
                // n=4: sensore carta (bit 5-6 = carta esaurita)
                var carta = await ChiediStatoAsync(4);
                if (carta.HasValue && (carta.Value & 0x60) == 0x60) return "Carta finita.";
            }
            catch (Exception ex) { LogManager.Debug($"Controllo stato {_ip}: {ex.Message}"); }
            return null;
        }

        private async Task<byte?> ChiediStatoAsync(byte n)
        {
            await _stream.WriteAsync(new byte[] { 0x10, 0x04, n }, 0, 3);
            await _stream.FlushAsync();
            var buf = new byte[1];
            using var cts = new CancellationTokenSource(800);
            try { int letti = await _stream.ReadAsync(buf, 0, 1, cts.Token); return letti == 1 ? buf[0] : null; }
            catch (OperationCanceledException) { return null; }
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
