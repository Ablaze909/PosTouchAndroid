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

    public class EpsonResponse
    {
        public bool Success { get; set; }
        public bool IsError { get; set; }
        public int Header1 { get; set; }
        public int Header2 { get; set; }
        public string Data { get; set; } = "";
        public int ErrorCode { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string RawHex { get; set; } = "";
    }

    public class EpsonConnector : IFiscalConnector
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

        // ====================== VENDITA ======================

        public Task<bool> SelezionaOperatoreAsync(int numero)
        {
            _op = Math.Clamp(numero, 1, 12);
            return Task.FromResult(true);
        }

        /// <summary>
        /// H1=1; H2=080 – PRINT REC ITEM
        /// TX: OP(2) + DESCR(1-38) + QTY(7) + PRICE(9) + DEP(2) + L/R(1)
        /// La stampante calcola la lunghezza DESCR dal totale: descr_len = data_len - 2 - 7 - 9 - 2 - 1
        /// </summary>
        public async Task<bool> VendiRepartoAsync(int reparto, string descrizione, int prezzoCent, decimal qta = 1)
        {
            // DESCR variabile (max 38), seguita da campi fissi alla fine
            // FIX BUG-C3: Sanitizza descrizione — caratteri di controllo (STX, ETX, ecc.)
            // corrompono il frame PDU; caratteri non-ASCII alterano il checksum.
            string descr = SanitizzaDescrizioneEpson(descrizione);

            string data = Op2                    // OP: 2 bytes
                        + descr                  // DESCR: 1-38 bytes (variabile)
                        + Qty7(qta)              // QTY: 7 bytes (0001000 = 1.000)
                        + Amt9(prezzoCent)        // PRICE: 9 bytes (centesimi)
                        + Num(reparto, 2)         // DEP: 2 bytes (01-99)
                        + "1";                    // L/R: 1 byte (1 = display first 20 chars)

            LogManager.Info($"Epson VENDITA: rep={reparto} \"{descrizione}\" €{prezzoCent / 100m:F2} x{qta}");
            return await OkAsync(1, 80, data);
        }

        /// <summary>
        /// FIX BUG-C3: Sanitizza la descrizione prodotto per il protocollo Epson.
        /// - Sostituisce virgolette doppie " con apice ' (separatore di campo protocollo, pag. 9)
        /// - Blocca caratteri di controllo 0x00-0x1F (STX=0x02, ETX=0x03 rompono il frame PDU)
        /// - Blocca DEL 0x7F
        /// - Ammette ASCII stampabile 0x20-0x7E + OEM-437 estesi 0x80-0xFF (pag. 8 manuale:
        ///   "Le descrizioni possono contenere tutti i caratteri estesi fino a 0xFFh")
        /// - Tronca a 38 caratteri (limite vendita, pag. 16)
        /// </summary>
        private static string SanitizzaDescrizioneEpson(string descrizione)
        {
            if (string.IsNullOrEmpty(descrizione)) return "ARTICOLO";
            
            // " è separatore di campo nel protocollo XON-XOFF (pag. 9 manuale)
            var desc = descrizione.Replace('"', '\'');
            
            // Blocca solo control chars (0x00-0x1F) e DEL (0x7F)
            // Il protocollo ammette OEM-437 estesi (0x80-0xFF) per lettere accentate, €, ecc.
            desc = new string(desc.Where(c => c >= 0x20 && c != 0x7F).ToArray());
            
            if (string.IsNullOrWhiteSpace(desc)) desc = "ARTICOLO";
            if (desc.Length > 38) desc = desc.Substring(0, 38);
            if (desc.Length < 1) desc = " ";
            
            return desc;
        }

        /// <summary>
        /// H1=1; H2=086 – PRINT REC SUBTOTAL
        /// TX: OP(2) + P/D(1) + N.USED(2)
        /// P/D: 0=print+display, 1=print, 2=display, 3=read only
        /// N.USED: "00" (spare bytes)
        /// </summary>
        public async Task<bool> SubtotaleAsync()
        {
            string data = Op2 + "0" + "00"; // P/D=0 (stampa+display), spare=00
            return await OkAsync(1, 86, data);
        }

        /// <summary>
        /// H1=1; H2=078 – PRINT REC TEXT
        /// TX: OP(2) + TYPE(1) + ROW(2) + FONT(1) + TEXT(1-46)
        /// TYPE 4 = testo libero nel corpo documento, ROW=00, FONT=1 (normal)
        /// </summary>
        public async Task<bool> StampaMessaggioAsync(string messaggio)
        {
            // FIX R1: Sanitizza messaggio — stessa protezione di VendiReparto
            // Limite 46 chars per TESTO gestionale (pag. 16 manuale Epson)
            string text = messaggio ?? "";
            text = text.Replace('"', '\'');
            text = new string(text.Where(c => c >= 0x20 && c != 0x7F).ToArray());
            if (text.Length > 46) text = text.Substring(0, 46);
            if (text.Length < 1) text = " ";

            string data = Op2
                        + "4"       // TYPE: 4 = additional description in document body
                        + "00"      // ROW: 00 per tipo 4
                        + "1"       // FONT: 1 = normal
                        + text;     // TEXT: 1-46 bytes

            return await OkAsync(1, 78, data);
        }

        // ====================== SCONTI ======================
        // H1=1; H2=083 – PRINT REC ADJUSTMENT
        // TX: OP(2) + DESCR(1-38) + AMN(9) + TYPE(1) + DEP(2) + L/R(1)
        //
        // TYPE: 0=sconto su ultima riga, 1=sconto su subtotale CON stampa subtotale,
        //       2=sconto su subtotale SENZA stampa, 5=maggiorazione ultima riga,
        //       6=maggiorazione subtotale CON stampa, 7=maggiorazione subtotale SENZA stampa,
        //       3=sconto su reparto specifico, 8=maggiorazione su reparto specifico

        public async Task<bool> ScontoPercentualeRigaAsync(decimal percentuale)
        {
            // Epson non supporta sconti % nativi (manuale: "managed by your retail application").
            // Leggiamo il subtotale, calcoliamo l'importo, inviamo sconto fisso sull'ultima riga.
            int importoCent = await CalcolaScontoPercentualeAsync(percentuale);
            if (importoCent <= 0)
            {
                // FIX BUG-21: ritornare false (non true) — lo sconto non è stato applicato.
                // Prima del fix: return true causava una discrepanza fiscale silenziosa:
                // il DB salvava il totale scontato ma l'RT stampava il totale pieno.
                UltimoErrore = $"Impossibile leggere il subtotale dall'RT per calcolare lo sconto {percentuale:F0}%.";
                LogManager.Error($"Epson: sconto %riga={percentuale} → importo=0, restituisco ERRORE al chiamante");
                return false;
            }

            // TYPE=0: sconto fisso sull'ultima riga venduta
            string descr = $"SCONTO {percentuale:0.##}%";
            string data = Op2 + descr + Amt9(importoCent) + "0" + "01" + "1";
            return await OkAsync(1, 83, data);
        }

        public async Task<bool> ScontoPercentualeTotaleAsync(decimal percentuale)
        {
            // Epson non supporta sconti % nativi.
            // Leggiamo il subtotale, calcoliamo l'importo, inviamo sconto fisso sul subtotale.
            int importoCent = await CalcolaScontoPercentualeAsync(percentuale);
            if (importoCent <= 0)
            {
                // FIX BUG-21: ritornare false — identico a ScontoPercentualeRigaAsync
                UltimoErrore = $"Impossibile leggere il subtotale dall'RT per calcolare lo sconto {percentuale:F0}%.";
                LogManager.Error($"Epson: sconto %tot={percentuale} → importo=0, restituisco ERRORE al chiamante");
                return false;
            }

            // TYPE=2: sconto sul subtotale SENZA ristampare riga subtotale
            // (InviaScontiRTAsync ha già chiamato SubtotaleAsync → già stampato)
            string descr = $"SCONTO {percentuale:0.##}%";
            string data = Op2 + descr + Amt9(importoCent) + "2" + "01" + "1";
            return await OkAsync(1, 83, data);
        }

        /// <summary>
        /// Legge il subtotale corrente dalla stampante (1-086 P/D=3 = read only)
        /// e calcola l'importo dello sconto in centesimi.
        /// </summary>
        private async Task<int> CalcolaScontoPercentualeAsync(decimal percentuale)
        {
            try
            {
                // 1-086 con P/D=3: legge subtotale senza stampare/visualizzare
                string readData = Op2 + "3" + "00"; // P/D=3 (read), spare=00
                var r = await SendAsync(1, 86, readData);

                if (!r.Success || r.Data.Length < 12)
                {
                    LogManager.Error($"Epson: impossibile leggere subtotale per sconto % (data={r.Data})");
                    return 0;
                }

                // RX: OP(2) + TYPE(1) + AMN(9) — AMN è il subtotale in centesimi
                string amnStr = r.Data.Substring(3, 9);
                
                // AMN potrebbe essere negativo (prefisso "-")
                if (amnStr.StartsWith("-"))
                {
                    LogManager.Warning($"Epson: subtotale negativo ({amnStr}), non applico sconto %");
                    return 0;
                }

                if (!int.TryParse(amnStr, out int subtotaleCent) || subtotaleCent <= 0)
                {
                    LogManager.Warning($"Epson: subtotale non valido ({amnStr})");
                    return 0;
                }

                int scontoCent = (int)Math.Round(subtotaleCent * percentuale / 100m, MidpointRounding.AwayFromZero);
                LogManager.Info($"Epson sconto {percentuale:F2}% su subtotale {subtotaleCent}c = {scontoCent}c");
                return scontoCent;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Epson CalcolaScontoPercentuale: {ex.Message}");
                return 0;
            }
        }

        public async Task<bool> ScontoValoreRigaAsync(int importoCent)
        {
            // TYPE=0: sconto fisso sull'ultima riga venduta
            string descr = "SCONTO";
            string data = Op2 + descr + Amt9(importoCent) + "0" + "01" + "1";
            //                 DESCR(6)   AMN(9)          TYPE DEP  L/R
            return await OkAsync(1, 83, data);
        }

        public async Task<bool> ScontovaloreTotaleAsync(int importoCent)
        {
            // FIX BUG-59: TYPE cambiato da 1 a 2. TYPE=1 ristampa la riga subtotale,
            // ma il caller (InviaScontiRTAsync) chiama sempre SubtotaleAsync() prima.
            // Con TYPE=1 il subtotale veniva stampato DUE VOLTE. TYPE=2 = sconto su subtotale
            // SENZA ristampa (coerente con ScontoPercentualeTotaleAsync che già usa TYPE=2).
            string descr = "SCONTO";
            string data = Op2 + descr + Amt9(importoCent) + "2" + "01" + "1";
            //                 DESCR(6)   AMN(9)          TYPE DEP  L/R
            return await OkAsync(1, 83, data);
        }

        // ====================== PAGAMENTI ======================
        // H1=1; H2=084 – PRINT REC TOTAL
        // TX: OP(2) + DESCR(1-38) + AMN(9) + TYPE(1) + IND(2) + L/R(1)
        //
        // TYPE: 0=Cash, 1=Cheque, 2=Credit Card, 3=Ticket singolo,
        //       4=Ticket multiplo, 5=Non riscosso, 6=Sconto a pagare
        // IND:  01-10 (indice pagamento)
        // AMN=000000000 → importo automatico (= totale dovuto)
        //
        // RX (chiuso): OP(2) + "1" + CHG(9) + DATE(6) + TIME(4) + FRN(4)
        // RX (parziale): OP(2) + "0" + MIS(9)

        public async Task<bool> ChiudiContantiAsync(int importoCent)
        {
            string descr = "CONTANTI";
            string data = Op2 + descr           // OP(2) + DESCR(8)
                        + Amt9(importoCent)      // AMN(9)
                        + "0"                    // TYPE: 0 = Cash
                        + "01"                   // IND: 01
                        + "1";                   // L/R: 1

            var resp = await SendAsync(1, 84, data);
            if (resp.Success) ParseChiusura(resp);
            else UltimoErrore = resp.IsError 
                ? $"Errore stampante: {resp.ErrorMessage} (cod. {resp.ErrorCode})" 
                : $"Errore comunicazione: {resp.ErrorMessage}";
            return resp.Success;
        }

        public async Task<bool> ChiudiContantiAutoAsync()
        {
            // AMN=000000000 → pagamento automatico importo esatto
            string descr = "CONTANTI";
            string data = Op2 + descr + Amt9(0) + "0" + "01" + "1";
            var resp = await SendAsync(1, 84, data);
            if (resp.Success) ParseChiusura(resp);
            else UltimoErrore = resp.IsError 
                ? $"Errore stampante: {resp.ErrorMessage} (cod. {resp.ErrorCode})" 
                : $"Errore comunicazione: {resp.ErrorMessage}";
            return resp.Success;
        }

        public async Task<bool> ChiudiCartaCreditoAsync()
        {
            string descr = "CARTA";
            string data = Op2 + descr + Amt9(0) + "2" + "01" + "1";
            //                                     TYPE=2 Credit Card
            var resp = await SendAsync(1, 84, data);
            if (resp.Success) ParseChiusura(resp);
            else UltimoErrore = resp.IsError 
                ? $"Errore stampante: {resp.ErrorMessage} (cod. {resp.ErrorCode})" 
                : $"Errore comunicazione: {resp.ErrorMessage}";
            return resp.Success;
        }

        /// <summary>
        /// Pagamento misto Epson: due chiamate 1-084 con flag L/R diverso.
        /// 
        /// PRIMO TENDER (contanti, parziale):
        ///   TX: OP(2) + "CONTANTI" + AMN(9) + TYPE="0" + IND="01" + L/R="0"
        ///   RX: OP(2) + "0" + MIS(9)  ← "0" = non chiuso, MIS = residuo da pagare
        /// 
        /// SECONDO TENDER (carta, finale):
        ///   TX: OP(2) + "CARTA" + AMN(9) + TYPE="2" + IND="01" + L/R="1"
        ///   RX: OP(2) + "1" + CHG(9) + DATE(6) + TIME(4) + FRN(4)  ← chiuso
        /// 
        /// NOTA: ParseChiusura va chiamata SOLO sulla risposta del secondo tender (L/R=1),
        /// perché il formato risposta è diverso per pagamento parziale.
        /// </summary>
        public async Task<bool> ChiudiMistoAsync(int contantiCent, int cartaCent)
        {
            // ── PRIMO TENDER: contanti, L/R=0 (parziale) ──
            string data1 = Op2 + "CONTANTI" + Amt9(contantiCent) + "0" + "01" + "0";
            //                                                      TYPE  IND   L/R=parziale
            var resp1 = await SendAsync(1, 84, data1);
            if (!resp1.Success)
            {
                UltimoErrore = resp1.IsError 
                    ? $"Errore stampante: {resp1.ErrorMessage} (cod. {resp1.ErrorCode})" 
                    : $"Errore comunicazione: {resp1.ErrorMessage}";
                LogManager.Error($"ChiudiMisto Epson: primo tender contanti fallito — {UltimoErrore}");
                return false;
            }

            // Risposta parziale: OP(2) + "0" + MIS(9)
            // "0" = scontrino ancora aperto, MIS = importo residuo
            if (resp1.Data?.Length >= 12)
            {
                string statusFlag = resp1.Data.Substring(2, 1);
                if (statusFlag == "0")
                {
                    string misStr = resp1.Data.Substring(3, 9);
                    LogManager.Debug($"ChiudiMisto Epson: parziale OK, residuo MIS={misStr} (flag={statusFlag})");
                }
                else
                {
                    // "1" = già chiuso con primo pagamento (contanti >= totale — non dovrebbe accadere nel misto)
                    LogManager.Warning($"ChiudiMisto Epson: primo tender ha già chiuso lo scontrino (flag={statusFlag})");
                    ParseChiusura(resp1);
                    return true;
                }
            }

            // ── SECONDO TENDER: carta, L/R=1 (finale) ──
            string data2 = Op2 + "CARTA" + Amt9(cartaCent) + "2" + "01" + "1";
            //                                                TYPE  IND   L/R=finale
            var resp2 = await SendAsync(1, 84, data2);
            if (resp2.Success)
            {
                ParseChiusura(resp2); // Solo qui: formato completo con CHG+DATE+TIME+FRN
            }
            else
            {
                UltimoErrore = resp2.IsError 
                    ? $"Errore stampante: {resp2.ErrorMessage} (cod. {resp2.ErrorCode})" 
                    : $"Errore comunicazione: {resp2.ErrorMessage}";
                LogManager.Error($"ChiudiMisto Epson: secondo tender carta fallito — {UltimoErrore}");
            }
            return resp2.Success;
        }

        /// <summary>
        /// Parsa risposta 1-084 per estrarre FRN (numero documento).
        /// RX chiuso: OP(2) + "1" + CHG(9) + DATE(6) + TIME(4) + FRN(4) = 26 chars
        /// NOTA: La risposta 1-084 NON contiene il numero azzeramento (Z).
        /// Lo recuperiamo separatamente con 2-050 INDEX=27.
        /// </summary>
        private void ParseChiusura(EpsonResponse resp)
        {
            _ultimaChiusura = new RisultatoChiusuraRT { Confermato = true, RispostaRaw = resp.Data };

            try
            {
                string d = resp.Data;
                if (d.Length < 22)
                {
                    LogManager.Debug($"Epson chiusura: risposta corta ({d.Length}): \"{d}\"");
                    return;
                }

                // 1-084 response: DESCR(var) + flag(1) + value(9) + DATE(6) + TIME(4) + FRN(4) = 24 tail bytes
                // NOTA: FRN è spesso "0000" nelle Epson — il numero doc si legge con 1-070
                string frn = d.Substring(d.Length - 4, 4);
                string time = d.Substring(d.Length - 8, 4);
                string date = d.Substring(d.Length - 14, 6);

                // Salva sempre data e ora dalla risposta 1-084 (sono affidabili)
                _ultimaChiusura.DataEmissione = NormalizzaDataEpson(date);
                _ultimaChiusura.OraEmissione = time;
                _ultimaChiusura.TipoDocumento = "F";
                LogManager.Info($"Epson 1-084: DATA={_ultimaChiusura.DataEmissione} ORA={time} FRN={frn}");

                // FRN > 0 = bonus (alcune Epson lo compilano), altrimenti 1-070 lo leggerà
                if (int.TryParse(frn, out int docNum) && docNum > 0)
                {
                    _ultimaChiusura.NumeroDocumento = docNum;
                    LogManager.Info($"Epson 1-084: DOC={docNum} (da FRN)");
                }
                // Se FRN=0000 non è un errore — è normale per le Epson, il doc si legge con 1-070
            }
            catch (Exception ex)
            {
                LogManager.Warning($"Epson ParseChiusura: {ex.Message}");
            }
        }

        /// <summary>
        /// H1=2; H2=050 – GET DAILY DATA con INDEX=27 (DAILY CLOSURES / Z NUMBER)
        /// Usato per scopo di logging durante la connessione. 
        /// Il metodo principale è LeggiNumeroAzzeramentoAsync.
        /// </summary>

        // ====================== CASSETTO ======================

        /// <summary>H1=1; H2=050 – OPEN DRAWER. TX: OP(2)</summary>
        public async Task<bool> ApriCassettoAsync()
        {
            return await OkAsync(1, 50, Op2);
        }

        // ====================== FUNZIONI FISCALI ======================

        /// <summary>H1=3; H2=001 – PRINT Z REPORT. TX: OP(2)</summary>
        public async Task<bool> ChiusuraFiscaleAsync()
        {
            return await OkAsync(3, 1, Op2, TIMEOUT_PRINT);
        }

        /// <summary>H1=2; H2=001 – PRINT X REPORT. TX: OP(2)</summary>
        public async Task<bool> LetturaGiornalieraAsync()
        {
            return await OkAsync(2, 1, Op2, TIMEOUT_PRINT);
        }

        /// <summary>H1=2; H2=003 – PRINT DAILY DEPARTMENT TOTALS. TX: OP(2)</summary>
        public async Task<bool> RapportoRepartiLetturaAsync()
        {
            return await OkAsync(2, 3, Op2, TIMEOUT_PRINT);
        }

        /// <summary>H1=3; H2=002 – PRINT FINANCIAL DATA AND Z REPORT. TX: OP(2)</summary>
        public async Task<bool> RapportoRepartiAzzeramentoAsync()
        {
            return await OkAsync(3, 2, Op2, TIMEOUT_PRINT);
        }

        // ====================== STATO ======================

        /// <summary>
        /// H1=1; H2=074 – GET PRINTER STATUS. TX: OP(2)
        /// RX: OP(2) + CPU(5) + MF_STAT(1) + MF_REL(4) + STATUS(5)
        /// </summary>
        public async Task<string> LeggiStatoRegistratoreAsync()
        {
            var r = await SendAsync(1, 74, Op2);
            return r.Success ? r.Data : null;
        }

        /// <summary>H1=1; H2=138 – RT STATUS. TX: OP(2)</summary>
        public async Task<string> LeggiStatoRTAsync()
        {
            var r = await SendAsync(1, 138, Op2);
            return r.Success ? r.Data : null;
        }

        /// <summary>
        /// H1=1; H2=070 – GET COMMERCIAL DOCUMENT NUMBER. TX: OP(2)
        /// RX: OP(2) + FR.N(4) + O/C(1)
        /// O/C: 0 = doc aperto (FR.N = corrente), 1 = chiuso (FR.N = prossimo)
        /// </summary>
        public async Task<(int Azzeramento, int Scontrino)?> LeggiProgressiviRTAsync()
        {
            var r = await SendAsync(1, 70, Op2);
            if (!r.Success || r.Data.Length < 7) return null;

            try
            {
                // OP(2) + FR.N(4) + O/C(1)
                string frnStr = r.Data.Substring(2, 4);
                if (!int.TryParse(frnStr, out int frn)) return null;

                int azzeramento = await LeggiNumeroAzzeramentoAsync();
                return (azzeramento, frn);
            }
            catch (Exception ex) { LogManager.Debug($"EpsonConnector errore: {ex.Message}"); return null; }
        }

        /// <summary>
        /// H1=1; H2=074 – STATUS byte 4 (pos 15 nel data)
        /// '0' = Monitor (no doc), altri = doc aperto
        /// </summary>
        public async Task<bool> HaScontrinoApertoAsync()
        {
            // NOTA: Il comando 1-074 GET PRINTER STATUS NON riporta in modo affidabile
            // lo stato documento sulle Epson (i byte di STATUS non corrispondono alla
            // documentazione generica — empiricamente byte [15] è sempre '1' anche senza doc aperti).
            //
            // Questo metodo controlla SOLO lo stato hardware (coperchio/carta).
            // Per verificare se c'è un documento aperto, si usa il metodo "probe":
            // tentare 1-028 VOID → se Error 11 "not allowed" = nessun doc aperto.
            try
            {
                var r = await SendAsync(1, 74, Op2);
                if (!r.Success || r.Data.Length < 13) return false;

                // Posizione [12] = stato hardware stampante (verificato empiricamente):
                // '0' = OK, '1' = carta quasi finita, '2' = offline, '3' = offline (coperchio+carta)
                char byte1 = r.Data[12];
                
                string statoHw = byte1 switch
                {
                    '0' => "OK",
                    '1' => "carta quasi finita",
                    _ => $"OFFLINE (coperchio/carta, cod={byte1})"
                };
                
                LogManager.Debug($"Epson HW STATUS: byte1='{byte1}' → {statoHw}");

                // byte1 >= '2' = stampante offline (coperchio aperto, carta esaurita, o combinazione)
                if (byte1 >= '2')
                {
                    UltimoErrore = "COPERCHIO APERTO o CARTA ESAURITA";
                    return true; // Segnala come "problematico" — richiede intervento
                }

                return false; // Stampante OK — non possiamo determinare doc state da 1-074
            }
            catch (Exception ex) { LogManager.Debug($"EpsonConnector errore: {ex.Message}"); return false; }
        }

        // ====================== RECOVERY ======================

        /// <summary>H1=1; H2=088 – RESET PRINTER. TX: OP(2)</summary>
        public async Task<bool> ClearAsync()
        {
            return await OkAsync(1, 88, Op2);
        }

        /// <summary>H1=1; H2=028 – PRINT REC VOID (total void doc corrente). TX: OP(2)</summary>
        public async Task<bool> AnnullaScontrinoCorrenteAsync()
        {
            return await OkAsync(1, 28, Op2);
        }

        /// <summary>
        /// Analizza lo stato della stampante (byte 1-5 del campo STATUS in 1-074).
        /// Restituisce una descrizione leggibile dell'errore hardware, o null se tutto OK.
        /// </summary>
        public async Task<string> RilevaErroreHardwareAsync()
        {
            try
            {
                var r = await SendAsync(1, 74, Op2);
                if (!r.Success || r.Data.Length < 13) return "Stampante non risponde";

                // Posizione [12] = stato hardware (verificato empiricamente)
                // '0' = OK, '1' = carta quasi finita, '2'/'3' = offline (coperchio/carta)
                char byte1 = r.Data[12];

                // byte1 >= '2' = stampante offline (coperchio, carta, o combinazione)
                if (byte1 >= '2')
                    return "COPERCHIO APERTO o CARTA ESAURITA — chiudere il coperchio e verificare la carta";
                if (byte1 == '1')
                    LogManager.Warning("⚠️ Carta quasi esaurita — sostituire presto il rotolo");

                return null; // Nessun errore hardware
            }
            catch (Exception ex) { LogManager.Debug($"EpsonConnector stato: {ex.Message}"); return "Impossibile leggere lo stato della stampante"; }
        }

        public async Task<bool> RecuperaScontrinoApertoAsync()
        {
            LogManager.Info("=== EPSON RECOVERY ===");

            // FASE 0: Controlla se c'è un errore hardware (coperchio, carta)
            string errHw = await RilevaErroreHardwareAsync();
            if (errHw != null)
            {
                UltimoErrore = errHw;
                LogManager.Error($"Recovery bloccato: {errHw}");
                return false;
            }

            for (int t = 1; t <= 3; t++)
            {
                try
                {
                    LogManager.Info($"Recovery tentativo {t}/3...");

                    // FASE 1: Prova 1-028 (PRINT REC VOID) — annulla il documento con numero
                    var rVoid = await SendAsync(1, 28, Op2);
                    
                    if (rVoid.Success || rVoid.ErrorCode == 2) // OK o warning carta
                    {
                        LogManager.Info("1-028 (VOID) OK — documento annullato");
                        await Task.Delay(2000); // Attesa stampa scontrino annullato
                        return true;
                    }
                    
                    // ERROR 11 = "Comando non consentito ora" = NON c'è nessun documento aperto!
                    // Questo è SUCCESSO: il documento è già stato chiuso/annullato
                    if (rVoid.ErrorCode == 11)
                    {
                        LogManager.Info("1-028 → Error 11 = nessun documento aperto → ✅ stampante già pulita");
                        return true;
                    }

                    // ERROR 3 = offline (coperchio/carta) — prova reset
                    LogManager.Warning($"1-028 fallito (err={rVoid.ErrorCode}) — provo 1-088 (RESET)...");

                    // FASE 2: Prova 1-088 (RESET PRINTER)
                    var rReset = await SendAsync(1, 88, Op2);
                    
                    if (rReset.Success || rReset.ErrorCode == 2)
                    {
                        LogManager.Info("1-088 (RESET) OK");
                        await Task.Delay(2000);
                        
                        // Dopo reset, verifica con un tentativo 1-028
                        var rVerifica = await SendAsync(1, 28, Op2);
                        if (rVerifica.ErrorCode == 11)
                        {
                            // Error 11 = nessun doc aperto = reset ha funzionato!
                            LogManager.Info("Verifica post-reset: Error 11 = ✅ documento pulito");
                            return true;
                        }
                        if (rVerifica.Success || rVerifica.ErrorCode == 2)
                        {
                            LogManager.Info("Verifica post-reset: VOID OK = documento annullato");
                            await Task.Delay(2000);
                            return true;
                        }
                        
                        LogManager.Warning($"Verifica post-reset: err={rVerifica.ErrorCode} — riprovo");
                    }
                    else if (rReset.ErrorCode == 3)
                    {
                        LogManager.Error("1-088 (RESET) fallito — stampante ancora offline");
                        
                        // Controlla se l'hardware è tornato OK
                        errHw = await RilevaErroreHardwareAsync();
                        if (errHw != null)
                        {
                            UltimoErrore = errHw;
                            LogManager.Error($"Hardware ancora in errore: {errHw}");
                            return false; // Inutile riprovare con HW in errore
                        }
                    }

                    // Riconnessione TCP al tentativo 2+
                    if (t >= 2)
                    {
                        LogManager.Info("Riconnessione TCP...");
                        Disconnect();
                        await Task.Delay(2000);
                        await ConnectAsync();
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Error($"Recovery t{t}: {ex.Message}");
                }
            }

            UltimoErrore = "Scontrino bloccato — spegnere e riaccendere la stampante";
            return false;
        }

        // ====================== ANNULLO / RESO ======================

        /// <summary>
        /// H1=9; H2=205 – REQUEST VOID REFUND STATUS DOC
        /// TX: VR(1) + S/N(11) + DATE(8) + NNNN(4) + ZZZZ(4) [NO OP!]
        /// RX: IS_V_R(1) + REC_REMAINING_AMOUNT(9)
        /// </summary>
        public async Task<Risultato50F> VerificaScontrinoCompletoAsync(int azzeramento, int numeroScontrino, DateTime? dataScontrino = null)
        {
            var ris = new Risultato50F();
            try
            {
                string matricola = await LeggiMatricolaAsync();

                // Formato data per Epson: DDMMYYYY. Se non fornita, usa "00000000" (potrebbe fallire)
                string dateStr = dataScontrino.HasValue
                    ? dataScontrino.Value.ToString("ddMMyyyy")
                    : "00000000";

                // ATTENZIONE: 9-205 NON ha campo OP!
                string data = "2"                              // VR: 2 = check if voidable
                            + Pad(matricola, 11)               // S/N
                            + dateStr                          // DATE: DDMMYYYY
                            + Num(numeroScontrino, 4)          // NNNN
                            + Num(azzeramento, 4);             // ZZZZ

                var r = await SendAsync(9, 205, data);

                if (!r.Success)
                {
                    ris.Esito = 9;
                    ris.EsitoDescrizione = r.IsError ? r.ErrorMessage : "Nessuna risposta";
                    return ris;
                }

                // RX: IS_V_R(1) + REC_REMAINING(9) — senza OP
                char isVr = r.Data.Length > 0 ? r.Data[0] : '9';
                ris.RispostaRaw = r.Data;
                
                // Epson IS_V_R: 0=annullabile (MPD corrente), 1=annullabile (altro MPD)
                // Mappiamo char→int. Epson '1' = annullabile (da altro MPD) → esito 9 (da altro RT, annullabile)
                int esitoNum = char.IsDigit(isVr) ? (isVr - '0') : 9;
                
                // Epson 0 e 1 = annullabile; rimappiamo Epson 1→0 perché Risultato50F.Annullabile controlla 0 o 9
                ris.Esito = (esitoNum == 1) ? 0 : esitoNum;
                
                ris.EsitoDescrizione = isVr switch
                {
                    '0' => "Annullabile (MPD corrente)",
                    '1' => "Annullabile (altro MPD)",
                    '2' => "Parametri fuori range",
                    '3' => "Già reso",
                    '4' => "Già annullato",
                    '5' => "È un documento di reso",
                    '6' => "È un documento di annullo",
                    _ => $"Errore ({isVr})"
                };
            }
            catch (Exception ex)
            {
                ris.Esito = 9;
                ris.EsitoDescrizione = ex.Message;
            }
            return ris;
        }

        public Task<bool> AnnullaScontrinoAsync(int azzeramento, int numeroScontrino)
            => AnnullaScontrinoAsync(azzeramento, numeroScontrino, DateTime.Today);

        /// <summary>
        /// Annullo via H1=1; H2=078 tipo 1 o 4:
        /// TX: OP(2) + TYPE("4") + ROW("00") + FONT("1") + TEXT
        /// TEXT = "VOID zzzz nnnn ddmmyyyy 99$%%ssssss"
        ///
        /// Risposta: OP 01-12 = semi-manuale (doc aperto), OP 51-62 = automatico (doc chiuso)
        /// </summary>
        public async Task<bool> AnnullaScontrinoAsync(int azzeramento, int numeroScontrino, DateTime dataScontrino)
        {
            try
            {
                string matricola = await LeggiMatricolaAsync();
                string voidText = $"VOID {Num(azzeramento, 4)} {Num(numeroScontrino, 4)} {dataScontrino:ddMMyyyy} {matricola}";
                LogManager.Info($"Epson ANNULLO: \"{voidText}\"");

                // H1=1; H2=078 — TYPE=4, ROW=00, FONT=1
                string data = Op2 + "4" + "00" + "1" + voidText;
                var resp = await SendAsync(1, 78, data, TIMEOUT_PRINT);

                if (!resp.Success) return false;

                // OP 51-62 = annullo automatico completato
                if (resp.Data.Length >= 2 && int.TryParse(resp.Data.Substring(0, 2), out int opResp) && opResp >= 51)
                {
                    LogManager.Info($"Annullo AUTOMATICO OK (op={opResp})");
                    return true;
                }

                // OP 01-12 = semi-manuale, doc aperto → chiudiamo
                LogManager.Info("Annullo semi-manuale — chiusura doc...");
                return await ChiudiContantiAutoAsync();
            }
            catch (Exception ex)
            {
                LogManager.Error($"Epson Annullo: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> IniziaResoAsync(int azzeramento, int numeroScontrino, DateTime? dataScontrino = null)
        {
            try
            {
                string matricola = await LeggiMatricolaAsync();
                DateTime dataEffettiva = dataScontrino ?? DateTime.Today;
                string resoText = $"REFUND {Num(azzeramento, 4)} {Num(numeroScontrino, 4)} {dataEffettiva:ddMMyyyy} {matricola}";

                string data = Op2 + "4" + "00" + "1" + resoText;
                return await OkAsync(1, 78, data, TIMEOUT_PRINT);
            }
            catch (Exception ex)
            {
                LogManager.Error($"Epson Reso: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Converte data Epson DDMMYY → "yyyy-MM-dd" normalizzato.
        /// </summary>
        private static string NormalizzaDataEpson(string ddmmyy)
        {
            if (string.IsNullOrEmpty(ddmmyy) || ddmmyy.Length < 6) return null;
            try
            {
                int dd = int.Parse(ddmmyy.Substring(0, 2));
                int mm = int.Parse(ddmmyy.Substring(2, 2));
                int yy = int.Parse(ddmmyy.Substring(4, 2));
                // FIX BUG-N3: guard esplicita — gemello di BUG-11 già fixato in RT3iConnector.
                // yy fuori range (es. yy=200 da risposta Epson malformata) causa
                // ArgumentOutOfRangeException in new DateTime(2200,...).
                if (yy < 0 || yy > 99)
                {
                    LogManager.Warning($"NormalizzaDataEpson: anno fuori range ({yy}), raw='{ddmmyy}'");
                    return null;
                }
                return new DateTime(2000 + yy, mm, dd).ToString("yyyy-MM-dd");
            }
            catch (Exception ex) { LogManager.Debug($"EpsonConnector errore: {ex.Message}"); return null; }
        }

        private async Task<string> LeggiMatricolaAsync()
        {
            try
            {
                var r = await SendAsync(1, 138, Op2);
                if (r.Success && r.Data.Length >= 13)
                    return r.Data.Substring(2, 11); // Dopo OP(2)
            }
            catch (Exception ex) { LogManager.Debug($"EpsonConnector query errore: {ex.Message}"); }
            return "00000000000";
        }

        // ====================== RISTAMPA ======================

        /// <summary>H1=1; H2=047 – REPRINT LAST COMMERCIAL DOCUMENT. TX: OP(2)</summary>
        public async Task<bool> StampaUltimoScontrinoAsync()
        {
            return await OkAsync(1, 47, Op2, TIMEOUT_PRINT);
        }

        public Task<bool> StampaPenultimoScontrinoAsync()
        {
            LogManager.Warning("Epson: StampaPenultimo non disponibile");
            return Task.FromResult(false);
        }

        /// <summary>
        /// H1=3; H2=098 – PRINT FROM MPD BY NUMBER
        /// TX: OP(2) + DATE(6) + N1(4) + N2(4)
        /// DATE = DDMMYY della chiusura Z in cui è stato emesso lo scontrino.
        ///
        /// FIX BUG-8: prima usava sempre DateTime.Today — se lo scontrino era di ieri
        /// o di giorni precedenti, Epson rispondeva errore 21 "dato inesistente".
        /// Ora usa la data reale passata dal chiamante (letta da DataEmissioneRT nel DB).
        /// Fallback a DateTime.Today solo se la data non è disponibile.
        /// </summary>
        public async Task<bool> StampaCopiaFiscaleAsync(int azzeramento, int numeroScontrino, DateTime? dataChiusuraZ = null)
        {
            string date = (dataChiusuraZ ?? DateTime.Today).ToString("ddMMyy");
            LogManager.Info($"Epson copia fiscale 3-098: Z={azzeramento} SC={numeroScontrino} DATA={date}");
            string data = Op2 + date + Num(numeroScontrino, 4) + Num(numeroScontrino, 4);
            return await OkAsync(3, 98, data, TIMEOUT_PRINT);
        }

        // ====================== TX FOOTER ======================

        public Task<bool> ConfiguraTxFooterJsonAsync()
        {
            // Epson non ne ha bisogno — le risposte sono strutturate nel PDU
            return Task.FromResult(true);
        }

        public async Task<RisultatoChiusuraRT> LeggiTxFooterAsync()
        {
            // NOTA: Epson NON ha TX_FOOTER come la 3i.
            // I dati scontrino si leggono con comandi nativi:
            //   1-084 → data/ora (FRN spesso "0000")
            //   1-070 → numero documento corrente
            //   2-050 → numero azzeramento (Z)

            var ris = _ultimaChiusura ?? new RisultatoChiusuraRT();

            // Se 1-084 non ha dato il numero documento, leggiamo con 1-070
            if (ris.NumeroDocumento <= 0)
            {
                try
                {
                    var r = await SendAsync(1, 70, Op2);
                    if (r.Success && r.Data.Length >= 7)
                    {
                        if (int.TryParse(r.Data.Substring(2, 4), out int frn) && frn > 0)
                        {
                            char oc = r.Data[6]; // '1' = doc aperto, '0' = chiuso
                            // FIX BUG-I4: Se OC='1' (doc aperto) subito dopo chiusura,
                            // il doc appena chiuso è frn-1. Ma se frn=1 il risultato è 0 → invalido.
                            // In quel caso usiamo frn e logghiamo warning per indagine.
                            if (oc == '1' && frn > 1)
                            {
                                ris.NumeroDocumento = frn - 1;
                            }
                            else if (oc == '1' && frn <= 1)
                            {
                                ris.NumeroDocumento = frn;
                                LogManager.Warning($"Epson 1-070: OC=1 ma frn={frn} (primo doc?) — uso frn come numero documento");
                            }
                            else
                            {
                                ris.NumeroDocumento = frn;
                            }
                            ris.Confermato = ris.NumeroDocumento > 0;
                            ris.TipoDocumento = "F";
                            LogManager.Info($"Epson 1-070: DOC={ris.NumeroDocumento} (FRN={frn}, OC={oc})");
                            
                            // Se mancano data/ora (1-084 non parsato), usa orologio PC
                            if (string.IsNullOrEmpty(ris.DataEmissione))
                            {
                                ris.DataEmissione = DateTime.Now.ToString("yyyy-MM-dd");
                                ris.OraEmissione = DateTime.Now.ToString("HHmm");
                            }
                        }
                    }
                }
                catch (Exception ex) { ris.Errore = ex.Message; }
            }

            // Leggi sempre azzeramento con 2-050 se mancante
            if (ris.NumeroAzzeramento <= 0)
                ris.NumeroAzzeramento = await LeggiNumeroAzzeramentoAsync();

            return ris;
        }

        /// <summary>
        /// H1=2; H2=050 – GET DAILY DATA con INDEX=27
        /// Legge il numero di chiusure Z effettuate. Il numero Z corrente = ZREP + 1
        /// TX: INDEX(2)="27" + NUMBER(2)="00"
        /// RX: INDEX(2) + NUMBER(2) + SIGN_I(1) + NUVAL(9) + SIGN_N(1) + ZREP(9) = 24 bytes
        /// </summary>
        private async Task<int> LeggiNumeroAzzeramentoAsync()
        {
            try
            {
                var r = await SendAsync(2, 50, "2700");
                if (!r.Success || r.Data.Length < 24)
                {
                    LogManager.Warning($"Epson 2-050/27: risposta corta ({r.Data?.Length}): \"{r.Data}\"");
                    return 0;
                }

                // RX DATA: INDEX(2) + NUMBER(2) + SIGN_I(1) + NUVAL(9) + SIGN_N(1) + ZREP(9)
                // ZREP inizia a posizione 15, 9 cifre
                string zrepStr = r.Data.Substring(15, 9);
                if (int.TryParse(zrepStr, out int zrep))
                {
                    int azzeramento = zrep + 1; // "Add 1 to ascertain the current Z number"
                    LogManager.Info($"Epson ZREP={zrep} → Azzeramento corrente={azzeramento}");
                    return azzeramento;
                }

                LogManager.Warning($"Epson 2-050/27: ZREP non parsabile \"{zrepStr}\"");
                return 0;
            }
            catch (Exception ex)
            {
                LogManager.Warning($"Epson LeggiNumeroAzzeramento: {ex.Message}");
                return 0;
            }
        }

        // ====================== APPENDICE ELIMINACODE BANCO ======================

        /// <summary>
        /// Epson: invia messaggio cortesia @40F PRIMA della chiusura scontrino.
        /// Il testo deve essere tra 39 e 46 caratteri. Supporta prefisso ~ per doppia altezza.
        /// Usa H2=078 con TYPE=6 (promotional message / additional trailer dopo logotipo).
        /// </summary>
        public async Task<bool> PreannunciaAppendiceAsync(int eliminacode)
        {
            string numStr = eliminacode.ToString("D3");
            // Riga 1: testo centrato con asterischi, padded a 42 chars
            string riga1 = $"*** BANCO - ELIMINACODE N. {numStr} ***";
            if (riga1.Length < 39) riga1 = riga1.PadRight(39);
            if (riga1.Length > 46) riga1 = riga1.Substring(0, 46);

            // TYPE=6: promotional message (additional trailer dopo logotipo)
            string data = Op2 + "6" + "00" + "1" + riga1;
            LogManager.Info($"Epson: PreannunciaAppendice @40F: '{riga1}' ({riga1.Length} chars)");
            return await OkAsync(1, 78, data);
        }

        /// <summary>
        /// Epson: no-op — il testo cortesia è già stato inviato in PreannunciaAppendice.
        /// </summary>
        public Task<bool> StampaAppendiceAsync(int eliminacode)
        {
            return Task.FromResult(true);
        }

        /// <summary>
        /// Epson: invia le righe come testo promozionale (H2=078 TYPE=6) PRIMA della chiusura.
        /// Le righe devono essere tra 39 e 46 char. Se più corte vengono paddate, se più lunghe troncate.
        /// </summary>
        public async Task<bool> PreannunciaAppendiceTestoLiberoAsync(string[] righe)
        {
            if (righe == null || righe.Length == 0) return true;
            LogManager.Info($"Epson: PreannunciaAppendiceTestoLibero — {righe.Length} righe");

            bool ok = true;
            foreach (var riga in righe)
            {
                if (string.IsNullOrWhiteSpace(riga)) continue;
                string r = riga.Trim();
                // Epson: H2=078 TYPE=6 vuole tra 39 e 46 char
                if (r.Length < 39) r = r.PadRight(39);
                if (r.Length > 46) r = r.Substring(0, 46);
                string data = Op2 + "6" + "00" + "1" + r;
                bool res = await OkAsync(1, 78, data);
                if (!res)
                {
                    LogManager.Warning($"Epson: appendice testo libero rifiutata per riga '{riga}'");
                    ok = false;
                }
            }
            return ok;
        }

        /// <summary>
        /// Epson: no-op — le righe sono già state inviate in PreannunciaAppendiceTestoLiberoAsync.
        /// </summary>
        public Task<bool> StampaAppendiceTestoLiberoAsync(string[] righe)
        {
            return Task.FromResult(true);
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
