using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PosTouch.Tests
{
    public enum GuastoEpson
    {
        Nessuno,
        /// <summary>Coperchio aperto: stato 1-074 con byte 12 = '2', ogni altro comando ERR 03 (offline).</summary>
        CoperchioAperto,
        /// <summary>Riga 1-080 rifiutata (ERR 17).</summary>
        RigaRifiutata,
        /// <summary>Chiusura eseguita con avviso carta in esaurimento (ERR 02): lo scontrino È uscito.</summary>
        CartaInEsaurimento,
        /// <summary>Chiusura eseguita ma la risposta non arriva.</summary>
        RispostaChiusuraPersa,
        /// <summary>Chiusura rifiutata (ERR 13): lo scontrino resta aperto.</summary>
        ChiusuraRifiutata
    }

    /// <summary>
    /// Stampante fiscale Epson finta su TCP: frame STX + CNT(2) + 'E' + H1(1) + H2(3) + DATI + CKS(2) + ETX,
    /// errori "ERR" + OP(2) + N(2). Comandi: 1-074 stato, 1-080 riga, 1-083 sconti, 1-084 pagamento (L/R = 1 chiude),
    /// 1-070 numero documento, 2-050 azzeramento, 1-028 annullo (ERR 11 se nessun documento), 1-088 reset, 1-050 cassetto.
    /// </summary>
    public sealed class FintoEpson : IDisposable
    {
        private const byte STX = 0x02, ETX = 0x03;
        private readonly TcpListener _ascolto;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        public int Porta { get; }
        public GuastoEpson Guasto { get; set; }
        public List<string> Comandi { get; } = new List<string>();
        public bool DocumentoAperto { get; private set; }
        public int ScontriniEmessi { get; private set; }
        public int Annulli { get; private set; }
        private int _numDoc = 41;

        public FintoEpson(GuastoEpson guasto = GuastoEpson.Nessuno)
        {
            Guasto = guasto;
            _ascolto = new TcpListener(IPAddress.Loopback, 0);
            _ascolto.Start();
            Porta = ((IPEndPoint)_ascolto.LocalEndpoint).Port;
            _ = Task.Run(AccettaAsync);
        }

        private async Task AccettaAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient c;
                try { c = await _ascolto.AcceptTcpClientAsync(_stop.Token); }
                catch { return; }
                _ = Task.Run(() => ServiAsync(c));
            }
        }

        private async Task ServiAsync(TcpClient client)
        {
            using (client)
            {
                var s = client.GetStream();
                var buf = new byte[4096];
                var frame = new List<byte>();
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        int n = await s.ReadAsync(buf, 0, buf.Length, _stop.Token);
                        if (n <= 0) return;
                        for (int i = 0; i < n; i++)
                        {
                            if (buf[i] == STX) frame.Clear();
                            frame.Add(buf[i]);
                            if (buf[i] != ETX) continue;
                            // payload = CNT(2) + 'E' + H1 + H2(3) + DATI, poi CKS(2)
                            string p = Encoding.ASCII.GetString(frame.ToArray(), 1, frame.Count - 4);
                            frame.Clear();
                            string cnt = p.Substring(0, 2);
                            int h1 = p[3] - '0';
                            int h2 = int.Parse(p.Substring(4, 3));
                            string dati = p.Substring(7);
                            string risposta = Esegui(h1, h2, dati);
                            if (risposta == null) continue;   // risposta persa
                            var r = Frame(cnt, risposta);
                            await s.WriteAsync(r, 0, r.Length);
                        }
                    }
                }
                catch { }
            }
        }

        private static byte[] Frame(string cnt, string apdu)
        {
            string payload = cnt + "E" + apdu;
            int sum = 0;
            foreach (char c in payload) sum += c;
            var f = new List<byte> { STX };
            f.AddRange(Encoding.ASCII.GetBytes(payload + (sum % 100).ToString("D2")));
            f.Add(ETX);
            return f.ToArray();
        }

        private static string Errore(string dati, int codice) => "ERR" + (dati.Length >= 2 ? dati.Substring(0, 2) : "01") + codice.ToString("D2");

        /// <returns>A.PDU di risposta, null = nessuna risposta.</returns>
        private string Esegui(int h1, int h2, string dati)
        {
            string cmd = $"{h1}-{h2:D3}";
            lock (Comandi) Comandi.Add(cmd + " " + dati);
            string ok(string d = "") => $"{h1}{h2:D3}" + (dati.Length >= 2 ? dati.Substring(0, 2) : "01") + d;

            if (cmd == "1-074")
            {
                var st = new StringBuilder(new string('0', 20));
                st[10] = Guasto == GuastoEpson.CoperchioAperto ? '2' : '0';   // con OP(2) davanti diventa il byte 12 dei dati
                return ok(st.ToString());
            }
            if (Guasto == GuastoEpson.CoperchioAperto) return Errore(dati, 3);

            switch (cmd)
            {
                case "1-080":
                    if (Guasto == GuastoEpson.RigaRifiutata) return Errore(dati, 17);
                    DocumentoAperto = true;
                    return ok();
                case "1-084":
                    if (!DocumentoAperto) return Errore(dati, 11);
                    bool finale = dati.EndsWith("1");
                    if (!finale) return ok("0" + "000000100");   // parziale: residuo
                    if (Guasto == GuastoEpson.ChiusuraRifiutata) return Errore(dati, 13);
                    DocumentoAperto = false;
                    ScontriniEmessi++;
                    _numDoc++;
                    if (Guasto == GuastoEpson.RispostaChiusuraPersa) return null;
                    if (Guasto == GuastoEpson.CartaInEsaurimento) return Errore(dati, 2);
                    return ok("1" + "000000000" + "261126" + "1830" + "0000");
                case "1-070":
                    return ok(_numDoc.ToString("D4") + (DocumentoAperto ? "1" : "0") + "000");
                case "2-050":
                    return $"{h1}{h2:D3}" + "2700" + "+" + "000000000" + "+" + "000000006";
                case "1-028":
                    if (!DocumentoAperto) return Errore(dati, 11);
                    DocumentoAperto = false; Annulli++;
                    return ok();
                default:
                    return ok();   // 1-083 sconti, 1-088 reset, 1-050 cassetto, 1-078 messaggi...
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _ascolto.Stop(); } catch { }
        }
    }
}
