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
    public partial class EpsonConnector
    {
        // ====================== PDU FRAME ======================

        private byte[] BuildFrame(int h1, int h2, string data)
        {
            _counter = (_counter % 99) + 1;
            string payload = _counter.ToString("D2") + "E"
                           + h1.ToString() + h2.ToString("D3") + data;

            int sum = 0;
            foreach (char c in payload) sum += c;
            string cks = (sum % 100).ToString("D2");

            var f = new List<byte>(payload.Length + 5);
            f.Add(STX);
            f.AddRange(Encoding.ASCII.GetBytes(payload));
            f.AddRange(Encoding.ASCII.GetBytes(cks));
            f.Add(ETX);
            return f.ToArray();
        }

        private EpsonResponse ParseFrame(byte[] buf, int len)
        {
            var r = new EpsonResponse();
            r.RawHex = BitConverter.ToString(buf, 0, len);

            int iStx = -1, iEtx = -1;
            for (int i = 0; i < len; i++)
            {
                if (buf[i] == STX && iStx < 0) iStx = i;
                if (buf[i] == ETX) iEtx = i;
            }

            if (iStx < 0 || iEtx < 0 || iEtx - iStx < 8)
            {
                r.ErrorMessage = $"Frame malformato (len={len})";
                return r;
            }

            int pLen = iEtx - iStx - 3; // payload senza CKS(2)
            if (pLen < 4)
            {
                r.ErrorMessage = "Payload troppo corto";
                return r;
            }

            string payload = Encoding.ASCII.GetString(buf, iStx + 1, pLen);
            string cksRx = Encoding.ASCII.GetString(buf, iEtx - 2, 2);

            int sum = 0;
            foreach (char c in payload) sum += c;
            string cksCalc = (sum % 100).ToString("D2");
            if (cksRx != cksCalc)
            {
                // FIX BUG-22: frame con checksum errato RIGETTATO — non più solo loggato.
                // Prima del fix: il frame corrotto veniva elaborato ugualmente, potendo accettare
                // una risposta di "chiusura scontrino OK" non autentica (dati corrotti via rete).
                LogManager.Error($"Epson CKS mismatch: rx={cksRx} calc={cksCalc} — frame rigettato");
                r.ErrorMessage = $"Errore checksum frame Epson (rx={cksRx}, calc={cksCalc})";
                return r;
            }

            // payload = CNT(2) + IDEN(1) + A.PDU
            string apdu = payload.Substring(3); // dopo CNT + "E"

            // Controllo errore: A.PDU = "ERR" + OP(2) + N(2)
            if (apdu.Length >= 7 && apdu.StartsWith("ERR"))
            {
                r.IsError = true;
                int.TryParse(apdu.Substring(5, 2), out int ec);
                r.ErrorCode = ec;
                r.ErrorMessage = ErrDesc(ec);
                return r;
            }

            // Risposta normale: H1(1) + H2(3) + DATA
            if (apdu.Length >= 4)
            {
                r.Header1 = apdu[0] - '0';
                int.TryParse(apdu.Substring(1, 3), out int h2);
                r.Header2 = h2;
                r.Data = apdu.Length > 4 ? apdu.Substring(4) : "";
                r.Success = true;
            }

            return r;
        }

        private static string ErrDesc(int c) => c switch
        {
            2 => "Carta in esaurimento",
            3 => "Offline (carta/coperchio)",
            7 => "Comando inesistente",
            8 => "Chiusura Z necessaria",
            9 => "Overflow campo",
            10 => "Parametro mancante",
            11 => "Comando non consentito ora",
            12 => "Importo negativo",
            13 => "Ticket supera subtotale",
            14 => "Giornata non aperta",
            15 => "Errore data/ora",
            16 => "JAVAPOS-UPOS non attivo",
            17 => "Operazione non consentita",
            18 => "Subtotale negativo",
            20 => "Errore DGFE/MPD",
            21 => "Dato inesistente",
            _ => $"Errore {c}"
        };

        // ====================== INVIO/RICEZIONE ======================

        private async Task<EpsonResponse> SendAsync(int h1, int h2, string data, int timeout = 0)
        {
            if (timeout <= 0) timeout = TIMEOUT_CMD;
            string cmd = $"{h1}-{h2:D3}";

            try
            {
                var frame = BuildFrame(h1, h2, data);

                LogManager.Info($">> EPSON [{cmd}] data_len={data.Length}");
                LogManager.Debug($"   TX hex: {BitConverter.ToString(frame)}");
                LogManager.Debug($"   TX ascii: {Encoding.ASCII.GetString(frame).Replace("\x02","<STX>").Replace("\x03","<ETX>")}");

                // risposte vecchie rimaste nel canale (es. arrivate dopo un timeout): via prima di scrivere,
                // altrimenti verrebbero lette come risposta a questo comando
                if (_stream.DataAvailable)
                {
                    var scarto = new byte[1024];
                    int buttati = 0;
                    while (_stream.DataAvailable && buttati < 64 * 1024)
                        buttati += _stream.Read(scarto, 0, scarto.Length);
                    LogManager.Warning($"EPSON [{cmd}]: scartati {buttati} byte di risposte vecchie prima dell'invio");
                }
                await _stream.WriteAsync(frame, 0, frame.Length);
                await _stream.FlushAsync();

                // FIX BUG-08: Sostituito busy-wait su DataAvailable con ReadAsync + CancellationToken.
                // Prima del fix: loop while(DateTime.Now < deadline) { if(DataAvailable) ... await Delay(20) }
                // generava centinaia di iterazioni sprecate e spike CPU durante ogni vendita.
                // Ora il thread dorme finché i dati non arrivano davvero, con timeout garantito.
                var buf = new byte[4096];
                int total = 0;
                using var cts = new CancellationTokenSource(timeout);

                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        int n = await _stream.ReadAsync(buf, total, buf.Length - total, cts.Token);
                        if (n <= 0) break;

                        total += n;

                        // Cerca ETX nel buffer ricevuto finora
                        for (int i = 0; i < total; i++)
                        {
                            if (buf[i] == ETX)
                            {
                                LogManager.Debug($"   RX hex ({total}B): {BitConverter.ToString(buf, 0, total)}");
                                var resp = ParseFrame(buf, total);

                                if (resp.IsError)
                                    LogManager.Error($"<< EPSON [{cmd}] ERRORE {resp.ErrorCode}: {resp.ErrorMessage}");
                                else if (resp.Success)
                                    LogManager.Info($"<< EPSON [{cmd}] OK data=\"{resp.Data}\"");
                                else
                                    LogManager.Warning($"<< EPSON [{cmd}] parse: {resp.ErrorMessage}");

                                return resp;
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Timeout scattato — cade nel blocco di gestione timeout sotto
                }

                // Timeout
                if (total > 0)
                {
                    LogManager.Error($"<< EPSON [{cmd}] TIMEOUT con {total}B parziali: {BitConverter.ToString(buf, 0, total)}");
                    return ParseFrame(buf, total);
                }

                LogManager.Error($"<< EPSON [{cmd}] TIMEOUT — nessun byte ricevuto in {timeout}ms");
                return new EpsonResponse { ErrorMessage = "Timeout — stampante non risponde" };
            }
            catch (Exception ex)
            {
                LogManager.Error($"!! EPSON [{cmd}] eccezione: {ex.Message}");
                return new EpsonResponse { ErrorMessage = ex.Message };
            }
        }

        private async Task<bool> OkAsync(int h1, int h2, string data, int timeout = 0)
        {
            var r = await SendAsync(h1, h2, data, timeout);
            if (r.IsError)
            {
                // Codice 2 = "CARTA SCONTRINO" = warning carta in esaurimento
                // Il comando è stato comunque eseguito, non è un errore fatale
                if (r.ErrorCode == 2)
                {
                    LogManager.Warning("Epson: carta in esaurimento (warning, comando eseguito)");
                    UltimoErrore = "Attenzione: carta in esaurimento";
                    return true; // Warning, non errore
                }

                UltimoErrore = $"Errore stampante: {r.ErrorMessage} (cod. {r.ErrorCode})";
            }
            else if (!r.Success)
            {
                UltimoErrore = $"Errore comunicazione: {r.ErrorMessage}";
            }
            return r.Success;
        }

        // ====================== HELPERS ======================

        private string Op2 => _op.ToString("D2");
        private static string Amt9(int cents) => Math.Abs(cents).ToString().PadLeft(9, '0');
        // FIX BUG#3: arrotondamento corretto (era (int) cast diretto → troncamento)
        private static string Qty7(decimal q) => ((int)Math.Round(q * 1000m, MidpointRounding.AwayFromZero)).ToString().PadLeft(7, '0');
        private static string Num(int v, int w) => v.ToString().PadLeft(w, '0');
        private static string Pad(string s, int n)
        {
            s = s ?? "";
            return s.Length >= n ? s.Substring(0, n) : s.PadRight(n);
        }
    }
}
