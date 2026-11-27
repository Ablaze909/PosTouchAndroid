using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PosTouch.Tests
{
    /// <summary>Guasti che il registratore 3i finto sa simulare.</summary>
    public enum Guasto3i
    {
        Nessuno,
        /// <summary>Coperchio aperto / carta finita: NAK a ogni comando, niente viene eseguito.</summary>
        CoperchioAperto,
        /// <summary>Riga di vendita rifiutata con XOFF (es. reparto non programmato).</summary>
        RigaRifiutata,
        /// <summary>Pagamento rifiutato con XOFF: lo scontrino resta aperto.</summary>
        ChiusuraRifiutata,
        /// <summary>Chiude lo scontrino ma la risposta TX_FOOTER non arriva.</summary>
        FooterPerso,
        /// <summary>Carta finita proprio durante la chiusura: NAK, scontrino ancora aperto, nessun footer.</summary>
        CartaFinitaInChiusura,
        /// <summary>Chiude lo scontrino e poi la rete cade (connessione chiusa, niente footer).</summary>
        ReteCadeDopoChiusura,
        /// <summary>La rete cade a metà delle righe.</summary>
        ReteCadeSulleRighe,
        /// <summary>Non accetta la configurazione TX_FOOTER (modelli vecchi): XOFF poi XON.</summary>
        FooterNonSupportato,
        /// <summary>Lo stato 89F non risponde.</summary>
        StatoMuto,
        /// <summary>Il coperchio si apre dopo la prima riga: da lì in poi NAK a tutto (finché il test non lo richiude).</summary>
        CoperchioSulleRighe
    }

    /// <summary>
    /// Registratore telematico 3i finto su TCP (127.0.0.1, porta libera), per provare il driver vero
    /// (<see cref="PosTouch.Core.Fiscale.RT3iConnector"/>) senza registratore e senza carta.
    /// Capisce i comandi usati dalla cassa: righe R, pagamenti T (anche parziali H1T), sconti M, subtotale =,
    /// 89F (stato: byte 14 = A aperto / C chiuso), 25F (annullo), K, c, O, "3"901F (TX_FOOTER JSON), 40F-42F, @, a.
    /// </summary>
    public sealed class Finto3i : IDisposable
    {
        private const byte XON = 0x11, XOFF = 0x13, NAK = 0x15;
        private readonly TcpListener _ascolto;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly object _lock = new object();

        public int Porta { get; }
        public Guasto3i Guasto { get; set; }
        /// <summary>Comandi ricevuti, in ordine (es. "\"CAFFE\"120H1R", "1T").</summary>
        public List<string> Comandi { get; } = new List<string>();
        public bool ScontrinoAperto { get; set; }
        public int ScontriniEmessi { get; private set; }
        public int Annulli { get; private set; }
        public int Connessioni { get; private set; }

        private bool _footerJson;
        private long _totaleCent, _pagatoCent;
        private int _numDoc = 41;

        public Finto3i(Guasto3i guasto = Guasto3i.Nessuno)
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
                lock (_lock) Connessioni++;
                _ = Task.Run(() => ServiAsync(c));
            }
        }

        private async Task ServiAsync(TcpClient client)
        {
            using (client)
            {
                var s = client.GetStream();
                var buf = new byte[1024];
                var cmd = new StringBuilder();
                bool inStringa = false;
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        int n = await s.ReadAsync(buf, 0, buf.Length, _stop.Token);
                        if (n <= 0) return;
                        for (int i = 0; i < n; i++)
                        {
                            char ch = (char)buf[i];
                            cmd.Append(ch);
                            if (ch == '"') { inStringa = !inStringa; continue; }
                            if (inStringa) continue;
                            // un comando 3i finisce con una lettera (H e * sono separatori dentro il comando), '=' o '@'
                            bool fine = ch == '=' || ch == '@' || (char.IsLetter(ch) && ch != 'H');
                            if (!fine) continue;
                            string comando = cmd.ToString();
                            cmd.Clear();
                            bool continua = await EseguiAsync(s, client, comando);
                            if (!continua) return;
                        }
                    }
                }
                catch { /* connessione chiusa dal client o dal test */ }
            }
        }

        /// <returns>false = chiudi la connessione (rete caduta).</returns>
        private async Task<bool> EseguiAsync(NetworkStream s, TcpClient client, string c)
        {
            lock (_lock) Comandi.Add(c);
            async Task Invia(params byte[] b) { await s.WriteAsync(b, 0, b.Length); await s.FlushAsync(); }
            async Task Xoff() { await Invia(XOFF); _ = Task.Delay(300).ContinueWith(_ => { try { s.Write(new[] { XON }, 0, 1); } catch { } }); }

            if (c.EndsWith("89F"))
            {
                if (Guasto == Guasto3i.StatoMuto) return true;
                // 39 byte senza terminatore; byte 14 = stato scontrino
                var r = Encoding.ASCII.GetBytes(new string('0', 39));
                r[14] = (byte)(ScontrinoAperto ? 'A' : 'C');
                await Invia(r);
                return true;
            }
            if (Guasto == Guasto3i.CoperchioAperto) { await Invia(NAK); return true; }

            if (c.EndsWith("901F"))
            {
                if (Guasto == Guasto3i.FooterNonSupportato) await Xoff(); else _footerJson = true;
                return true;
            }
            if (c.EndsWith("25F")) { if (ScontrinoAperto) { ScontrinoAperto = false; Annulli++; _totaleCent = _pagatoCent = 0; } return true; }
            char ultimo = c[c.Length - 1];
            switch (ultimo)
            {
                case 'R':
                    if (Guasto == Guasto3i.RigaRifiutata) { await Xoff(); return true; }
                    if (Guasto == Guasto3i.CoperchioSulleRighe && ScontrinoAperto) { Guasto = Guasto3i.CoperchioAperto; await Invia(NAK); return true; }
                    if (Guasto == Guasto3i.ReteCadeSulleRighe && ScontrinoAperto) { client.Close(); return false; }
                    ScontrinoAperto = true;
                    _totaleCent += ImportoRiga(c);
                    return true;
                case 'M':
                    if (c.EndsWith("H3M") || c.EndsWith("H4M")) _totaleCent -= LeggiNumero(c, 'H');
                    else if (c.Contains("*")) _totaleCent -= (long)Math.Round(_totaleCent * LeggiNumero(c, '*') / 10000m);
                    return true;
                case 'T':
                    return await PagamentoAsync(s, client, c, Invia, Xoff);
                default:
                    return true;   // O, K, c, =, @, a, 40F-42F: accettati senza risposta
            }
        }

        private async Task<bool> PagamentoAsync(NetworkStream s, TcpClient client, string c, Func<byte[], Task> invia, Func<Task> xoff)
        {
            if (!ScontrinoAperto) { await xoff(); return true; }
            if (Guasto == Guasto3i.ChiusuraRifiutata) { await xoff(); return true; }
            if (Guasto == Guasto3i.CartaFinitaInChiusura)
            {
                // il comando parte, poi la stampante si ferma: NAK in ritardo (oltre gli 80 ms del controllo XOFF)
                await Task.Delay(250);
                await invia(new[] { NAK });
                return true;
            }
            long importo = c.Contains("H") ? LeggiNumero(c, 'H') : _totaleCent - _pagatoCent;
            _pagatoCent += importo;
            if (_pagatoCent < _totaleCent) return true;   // parziale (misto): resta aperto

            ScontrinoAperto = false;
            ScontriniEmessi++;
            _numDoc++;
            _totaleCent = _pagatoCent = 0;
            if (Guasto == Guasto3i.ReteCadeDopoChiusura) { client.Close(); return false; }
            if (Guasto == Guasto3i.FooterPerso || !_footerJson) return true;
            await Task.Delay(150);
            string json = "{\"data\":\"261126\",\"tempo\":\"1830\",\"tipodoc\":\"1\",\"numdoc\":\"" + _numDoc.ToString("0000") + "\",\"numazz\":\"0007\"}";
            await invia(Encoding.ASCII.GetBytes(json));
            return true;
        }

        /// <summary>"DESCR"qta*prezzoHrepR oppure "DESCR"prezzoHrepR → centesimi.</summary>
        private static long ImportoRiga(string c)
        {
            int fineStringa = c.LastIndexOf('"');
            string resto = c.Substring(fineStringa + 1);
            string prima = resto.Split('H')[0];
            decimal qta = 1;
            if (prima.Contains("*"))
            {
                var p = prima.Split('*');
                qta = decimal.Parse(p[0], CultureInfo.InvariantCulture);
                prima = p[1];
            }
            return (long)Math.Round(qta * long.Parse(prima));
        }

        /// <summary>Numero subito prima del separatore indicato (es. "250H1T" con 'H' → 250).</summary>
        private static long LeggiNumero(string c, char separatore)
        {
            int k = c.IndexOf(separatore);
            int j = k;
            while (j > 0 && char.IsDigit(c[j - 1])) j--;
            return long.Parse(c.Substring(j, k - j));
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _ascolto.Stop(); } catch { }
        }
    }
}
