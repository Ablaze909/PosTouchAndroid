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

    /// <summary>
    /// Risultato restituito dopo la chiusura di uno scontrino fiscale.
    /// Contiene i dati del TX_FOOTER inviato dall'RT.
    /// </summary>
    public class RisultatoChiusuraRT
    {
        /// <summary>True se l'RT ha confermato la chiusura con TX_FOOTER</summary>
        public bool Confermato { get; set; }
        
        /// <summary>Numero documento fiscale assegnato dall'RT</summary>
        public int NumeroDocumento { get; set; }
        
        /// <summary>Numero azzeramento corrente dell'RT</summary>
        public int NumeroAzzeramento { get; set; }
        
        /// <summary>Tipo documento: "F" = Fiscale, "G" = Gestionale</summary>
        public string TipoDocumento { get; set; }
        
        /// <summary>Data emissione (formato aammgg)</summary>
        public string DataEmissione { get; set; }
        
        /// <summary>Ora emissione (formato hhmm)</summary>
        public string OraEmissione { get; set; }
        
        /// <summary>Risposta raw dell'RT (per debug)</summary>
        public string RispostaRaw { get; set; }
        
        /// <summary>Eventuale errore</summary>
        public string Errore { get; set; }
    }

    /// <summary>
    /// Risultato del comando 50F: verifica stato scontrino per reso/annullo.
    /// Contiene esito, aliquote IVA e importi per IVA dello scontrino originale.
    /// Manuale Xon/Xoff pag. 20: risposta formato <esito><aliquota IVA 8x><importi IVA 8x>@@
    /// </summary>
    public class Risultato50F
    {
        /// <summary>
        /// Codice esito numerico (0-10).
        /// Manuale 3i pag 22:
        ///   0 = annullabile/rendibile (dati nel DGFE locale)
        ///   1 = non esistente
        ///   2 = non è doc di vendita
        ///   3 = già annullato/reso
        ///   4 = aliquote IVA cambiate
        ///   5 = aliquota IVA non associata a reparto
        ///   8 = già annullato su altro DGFE/RT
        ///   9 = da altro RT, non annullato/reso → annullo/reso OK (battute dall'operatore)
        ///  10 = già reso parzialmente → solo reso OK, annullo NO
        /// </summary>
        public int Esito { get; set; } = -1;
        
        /// <summary>True se lo scontrino è annullabile (esito 0 o 9)</summary>
        public bool Annullabile => Esito == 0 || Esito == 9;
        
        /// <summary>True se lo scontrino è rendibile (esito 0, 9, o 10)</summary>
        public bool Rendibile => Esito == 0 || Esito == 9 || Esito == 10;
        
        /// <summary>Descrizione leggibile dell'esito</summary>
        public string EsitoDescrizione { get; set; }
        
        /// <summary>Aliquote IVA con relativi importi (solo quelle > 0)</summary>
        public List<AliquotaIVA> Aliquote { get; set; } = new List<AliquotaIVA>();
        
        /// <summary>Totale scontrino originale (somma di tutti gli importi IVA)</summary>
        public decimal TotaleEuro { get; set; }
        
        /// <summary>Risposta raw dall'RT (per debug/log)</summary>
        public string RispostaRaw { get; set; }
    }
    
    public class AliquotaIVA
    {
        /// <summary>Codice aliquota: "22%", "10%", "04%", "ES", "EN", ecc.</summary>
        public string Codice { get; set; }
        
        /// <summary>Importo in euro per questa aliquota</summary>
        public decimal ImportoEuro { get; set; }
    }

    public class RT3iConnector : IFiscalConnector
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

        // ========== INVIO COMANDI CON CONTROLLO FLUSSO ==========

        /// <summary>
        /// Invia un comando all'RT con controllo flusso Xon/Xoff.
        /// Dopo l'invio, legge lo stream per intercettare eventuali XOFF (= errore).
        /// </summary>
        /// <returns>True se il comando è stato accettato, False se l'RT ha risposto XOFF</returns>
        private async Task<bool> SendCommandAsync(string comando)
        {
            try
            {
                // Se l'RT ha risposto NAK (errore hardware: coperchio/carta),
                // NON inviare altri comandi — serve intervento fisico
                if (_nakRicevuto)
                {
                    UltimoErrore = "COPERCHIO APERTO o CARTA ESAURITA — il registratore ha rifiutato il comando";
                    LogManager.Error($"!! Invio bloccato (NAK precedente): {comando}");
                    return false;
                }

                // FIX #6: Se l'RT ci ha mandato XOFF, aspetta XON prima di inviare
                if (_xoffRicevuto)
                {
                    LogManager.Warning("XOFF attivo — attendo XON dall'RT...");
                    bool riprendi = await AttendXonAsync(TIMEOUT_RISPOSTA_MS);
                    if (!riprendi)
                    {
                        UltimoErrore = "Registratore non risponde (timeout attesa XON)";
                        LogManager.Error("TIMEOUT attesa XON — l'RT non risponde");
                        return false;
                    }
                }

                LogManager.Debug($">> {comando}");
                
                var bytes = Encoding.ASCII.GetBytes(comando);
                await _stream.WriteAsync(bytes, 0, bytes.Length);
                await _stream.FlushAsync();
                
                // FIX #2/#6: Dopo l'invio, attendi brevemente e controlla se arriva XOFF
                await Task.Delay(DELAY_POST_COMANDO_MS);
                
                bool erroreRicevuto = await ControllaXoffAsync();
                if (erroreRicevuto)
                {
                    if (_nakRicevuto)
                    {
                        UltimoErrore = "COPERCHIO APERTO o CARTA ESAURITA — il registratore ha rifiutato il comando";
                        LogManager.Error($"!! RT ha risposto NAK (errore hardware) al comando: {comando}");
                    }
                    else
                    {
                        UltimoErrore = "Il registratore ha rifiutato il comando (XOFF)";
                        LogManager.Error($"!! RT ha risposto XOFF al comando: {comando}");
                    }
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Error($"!! ERRORE invio comando: {ex.Message}");
                throw;
            }
        }

        // ========== GESTIONE FLUSSO XON/XOFF (FIX #6) ==========

        /// <summary>
        /// Controlla se l'RT ha inviato XOFF (errore o buffer pieno).
        /// Non blocca: legge solo i dati già disponibili nello stream.
        /// IMPORTANTE: i byte che NON sono XON/XOFF/ACK/NAK vengono salvati in _pendingBytes
        /// per essere riletti da LeggiRispostaAsync (fix: evita di mangiare risposte 50F).
        /// </summary>
        private async Task<bool> ControllaXoffAsync()
        {
            try
            {
                if (_stream == null || !_stream.DataAvailable)
                    return false;

                var buffer = new byte[64];
                int bytesLetti = await _stream.ReadAsync(buffer, 0, buffer.Length);
                
                bool xoffTrovato = false;
                
                // Separa i byte di controllo (XON/XOFF/ACK/NAK) dai byte di dati (risposta RT)
                var datiByte = new byte[bytesLetti];
                int datiCount = 0;
                
                for (int i = 0; i < bytesLetti; i++)
                {
                    if (buffer[i] == XOFF)
                    {
                        _xoffRicevuto = true;
                        xoffTrovato = true;
                        LogManager.Warning($"<< XOFF ricevuto (byte {i}/{bytesLetti})");
                    }
                    else if (buffer[i] == XON)
                    {
                        _xoffRicevuto = false;
                        LogManager.Debug("<< XON ricevuto");
                    }
                    else if (buffer[i] == NAK)
                    {
                        // NAK (0x15) = il registratore ha RIFIUTATO il comando
                        // Cause: coperchio aperto, carta esaurita, testa alzata, errore stampante
                        _nakRicevuto = true;
                        xoffTrovato = true; // Trattato come errore (blocca invio comandi successivi)
                        LogManager.Error("<< NAK ricevuto — registratore in errore (coperchio/carta/stampante)");
                    }
                    else if (buffer[i] == ACK)
                    {
                        // ACK (0x06) = comando accettato — reset flag NAK
                        _nakRicevuto = false;
                        LogManager.Debug("<< ACK ricevuto");
                    }
                    else
                    {
                        // Byte di dati (parte della risposta): salvali nel buffer pending
                        datiByte[datiCount++] = buffer[i];
                    }
                }
                
                // Se ci sono byte di dati, salvali per LeggiRispostaAsync
                if (datiCount > 0)
                {
                    _pendingBytes = datiByte;
                    _pendingOffset = 0;
                    _pendingCount = datiCount;
                    
                    // Log hex dei primi byte per debug (individua caratteri di controllo invisibili)
                    var hexDump = string.Join(" ", datiByte.Take(Math.Min(datiCount, 10)).Select(b => $"0x{b:X2}"));
                    LogManager.Debug($"ControllaXoff: {datiCount} byte dati salvati in buffer pending [{hexDump}]");
                }
                
                return xoffTrovato;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Attende che l'RT invii XON (= buffer svuotato, pronto a ricevere).
        /// Usato dopo aver ricevuto XOFF.
        /// </summary>
        private async Task<bool> AttendXonAsync(int timeoutMs)
        {
            var scadenza = DateTime.Now.AddMilliseconds(timeoutMs);
            
            while (DateTime.Now < scadenza)
            {
                try
                {
                    if (_stream.DataAvailable)
                    {
                        int b = _stream.ReadByte();
                        if (b == XON)
                        {
                            _xoffRicevuto = false;
                            LogManager.Info("<< XON ricevuto — riprendo trasmissione");
                            return true;
                        }
                    }
                }
                catch (Exception ex) { LogManager.Debug($"Errore attesa XON: {ex.Message}"); }
                
                await Task.Delay(50);
            }
            
            return false;
        }

        /// <summary>
        /// Svuota eventuali dati residui nello stream (es. XON inviato all'accensione).
        /// </summary>
        private async Task SvuotaStreamAsync()
        {
            try
            {
                // Svuota anche il buffer pending
                _pendingBytes = null;
                _pendingOffset = 0;
                _pendingCount = 0;
                
                await Task.Delay(100);
                if (_stream.DataAvailable)
                {
                    var buffer = new byte[1024];
                    var bytesLetti = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    LogManager.Debug($"Svuotati {bytesLetti} byte residui dallo stream");
                }
            }
            catch (Exception ex) { LogManager.Debug($"Errore svuotamento stream: {ex.Message}"); }
        }

        // ========== LETTURA RISPOSTE DALL'RT (FIX #2) ==========

        /// <summary>
        /// Legge una risposta dall'RT terminata da CR+LF o da "@@".
        /// Usata per comandi che restituiscono dati (89F, 88F, 84F, 50F, TX_FOOTER).
        /// IMPORTANTE: legge prima eventuali byte pending da ControllaXoffAsync,
        /// poi continua a leggere dallo stream fino al terminatore.
        /// </summary>
        private async Task<string> LeggiRispostaAsync(int timeoutMs)
        {
            var sb = new StringBuilder();
            var scadenza = DateTime.Now.AddMilliseconds(timeoutMs);
            
            // FASE 1: Processa eventuali byte pending (letti da ControllaXoffAsync)
            if (_pendingBytes != null && _pendingCount > 0)
            {
                for (int i = _pendingOffset; i < _pendingOffset + _pendingCount; i++)
                {
                    int b = _pendingBytes[i];
                    if (b == XON) { _xoffRicevuto = false; continue; }
                    if (b == XOFF) { _xoffRicevuto = true; continue; }
                    if (b == NAK) { _nakRicevuto = true; continue; }
                    if (b == ACK) { _nakRicevuto = false; continue; }
                    // Scarta TUTTI i byte di controllo (< 0x20) tranne CR e LF (terminatori)
                    // Il manuale 3i specifica risposte in range 020H-07AH
                    if (b < 0x20 && b != CR && b != LF) continue;
                    
                    sb.Append((char)b);
                }
                
                // Svuota il buffer pending
                _pendingBytes = null;
                _pendingOffset = 0;
                _pendingCount = 0;
                
                // Controlla se i byte pending già contengono un terminatore
                var testoPending = sb.ToString();
                if (testoPending.EndsWith("\r\n"))
                {
                    var risposta = testoPending.TrimEnd('\r', '\n');
                    LogManager.Debug($"<< Risposta (pending+CR/LF): {risposta}");
                    return risposta;
                }
                if (testoPending.EndsWith("@@"))
                {
                    var risposta = testoPending.Substring(0, testoPending.Length - 2);
                    LogManager.Debug($"<< Risposta (pending+@@): {risposta}");
                    return risposta;
                }
                if (testoPending.StartsWith("{") && testoPending.EndsWith("}"))
                {
                    LogManager.Debug($"<< TX_FOOTER JSON (pending): {testoPending}");
                    return testoPending;
                }
                
                LogManager.Debug($"Pending buffer ({testoPending.Length} chars): '{testoPending}'");
            }
            
            // FASE 2: Continua a leggere dallo stream
            while (DateTime.Now < scadenza)
            {
                try
                {
                    if (_stream.DataAvailable)
                    {
                        int b = _stream.ReadByte();
                        if (b == -1) break;
                        
                        // Gestione Xon/Xoff/NAK/ACK e caratteri di controllo
                        if (b == XON) { _xoffRicevuto = false; continue; }
                        if (b == XOFF) { _xoffRicevuto = true; continue; }
                        if (b == NAK) { _nakRicevuto = true; continue; }
                        if (b == ACK) { _nakRicevuto = false; continue; }
                        // Scarta TUTTI i byte di controllo (< 0x20) tranne CR e LF
                        if (b < 0x20 && b != CR && b != LF) continue;
                        
                        sb.Append((char)b);
                        
                        var testo = sb.ToString();
                        
                        // Terminatore: CR+LF (risposta standard)
                        if (testo.EndsWith("\r\n"))
                        {
                            var risposta = testo.TrimEnd('\r', '\n');
                            LogManager.Debug($"<< Risposta: {risposta}");
                            return risposta;
                        }
                        
                        // Terminatore: @@ (risposte 88F, 84F, 50F)
                        if (testo.EndsWith("@@"))
                        {
                            var risposta = testo.Substring(0, testo.Length - 2);
                            LogManager.Debug($"<< Risposta (@@): {risposta}");
                            return risposta;
                        }
                        
                        // Terminatore: } per JSON TX_FOOTER
                        if (testo.StartsWith("{") && testo.EndsWith("}"))
                        {
                            LogManager.Debug($"<< TX_FOOTER JSON: {testo}");
                            return testo;
                        }
                    }
                    else
                    {
                        await Task.Delay(50);
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Error($"Errore lettura risposta: {ex.Message}");
                    break;
                }
            }
            
            // Restituisci quello che abbiamo anche se non terminato
            var risultato = sb.ToString().Trim('\r', '\n', '\0');
            if (!string.IsNullOrEmpty(risultato))
            {
                LogManager.Debug($"<< Risposta (timeout/parziale): {risultato}");
                return risultato;
            }
            
            return null;
        }

        // ========== TX_FOOTER: CONFERMA CHIUSURA SCONTRINO (FIX #2) ==========
        // Dal manuale 3i pag 15-16:
        // TX_FOOTER viene inviato dall'RT al PC a fine scontrino.
        // Modalità JSON ("3"901F): restituisce
        //   {"data":"AAMMGG","tempo":"HHMM","tipodoc":"F","numdoc":N,"numazz":Z}
        // Questo è il MODO CORRETTO per sapere che l'RT ha emesso lo scontrino
        // e quale numero documento fiscale gli ha assegnato.

        /// <summary>
        /// Configura TX_FOOTER in modalità JSON.
        /// Deve essere chiamato UNA VOLTA dopo la connessione, prima della prima vendita.
        /// Il setting persiste nel registratore fino alla prossima modifica.
        /// </summary>
        public async Task<bool> ConfiguraTxFooterJsonAsync()
        {
            LogManager.Info("Configurazione TX_FOOTER modalità JSON...");
            return await SendCommandAsync($"\"3\"{ComandiRT.AbilitaNonFiscale}");
        }

        /// <summary>
        /// Legge il TX_FOOTER JSON inviato dall'RT dopo la chiusura dello scontrino.
        /// Restituisce i dati fiscali del documento emesso.
        /// </summary>
        public async Task<RisultatoChiusuraRT> LeggiTxFooterAsync()
        {
            var risultato = new RisultatoChiusuraRT();
            
            try
            {
                var risposta = await LeggiRispostaAsync(TIMEOUT_TX_FOOTER_MS);
                risultato.RispostaRaw = risposta;
                
                if (string.IsNullOrEmpty(risposta))
                {
                    risultato.Errore = "Nessuna risposta TX_FOOTER dall'RT (timeout)";
                    LogManager.Warning(risultato.Errore);
                    return risultato;
                }
                
                // Cerca il JSON nella risposta (potrebbe avere caratteri prima/dopo)
                int jsonStart = risposta.IndexOf('{');
                int jsonEnd = risposta.LastIndexOf('}');
                
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var json = risposta.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    LogManager.Info($"TX_FOOTER JSON: {json}");
                    
                    // Parse JSON: {"data":"AAMMGG","tempo":"HHMM","tipodoc":"F","numdoc":N,"numazz":Z}
                    var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    risultato.DataEmissione = NormalizzaData3i(root.TryGetProperty("data", out var d) ? d.GetString() : "");
                    risultato.OraEmissione = root.TryGetProperty("tempo", out var t) ? t.GetString() : "";
                    risultato.TipoDocumento = root.TryGetProperty("tipodoc", out var td) ? td.GetString() : "";
                    
                    // numdoc può essere int o string nel JSON
                    if (root.TryGetProperty("numdoc", out var nd))
                        risultato.NumeroDocumento = nd.ValueKind == JsonValueKind.Number 
                            ? nd.GetInt32() 
                            : int.TryParse(nd.GetString(), out int n) ? n : 0;
                    
                    // numazz può essere int o string nel JSON
                    if (root.TryGetProperty("numazz", out var na))
                        risultato.NumeroAzzeramento = na.ValueKind == JsonValueKind.Number 
                            ? na.GetInt32() 
                            : int.TryParse(na.GetString(), out int a) ? a : 0;
                    
                    risultato.Confermato = risultato.NumeroDocumento > 0;
                    
                    LogManager.Info($"TX_FOOTER CONFERMATO: Doc={risultato.NumeroDocumento}, " +
                                   $"Azz={risultato.NumeroAzzeramento}, Tipo={risultato.TipoDocumento}");
                }
                else
                {
                    // Prova formato non-JSON (tipo RT): "AAMMGGHHMMF00001A00056"
                    if (risposta.Length >= 16)
                    {
                        risultato.DataEmissione = NormalizzaData3i(risposta.Substring(0, 6));
                        risultato.OraEmissione = risposta.Substring(6, 4);
                        risultato.TipoDocumento = risposta.Substring(10, 1);
                        if (int.TryParse(risposta.Substring(11, 5), out int numDoc))
                            risultato.NumeroDocumento = numDoc;
                        
                        // Se c'è anche azzeramento (formato RT = 21 chars con "A" + 5 cifre)
                        if (risposta.Length >= 22 && risposta[16] == 'A')
                        {
                            if (int.TryParse(risposta.Substring(17, 5), out int numAzz))
                                risultato.NumeroAzzeramento = numAzz;
                        }
                        
                        risultato.Confermato = risultato.NumeroDocumento > 0;
                        LogManager.Info($"TX_FOOTER (formato RT): Doc={risultato.NumeroDocumento}, Azz={risultato.NumeroAzzeramento}");
                    }
                    else
                    {
                        risultato.Errore = $"TX_FOOTER non parsabile: '{risposta}'";
                        LogManager.Warning(risultato.Errore);
                    }
                }
            }
            catch (Exception ex)
            {
                risultato.Errore = $"Errore lettura TX_FOOTER: {ex.Message}";
                LogManager.Error(risultato.Errore);
            }
            
            return risultato;
        }

        // ========== COMANDI VENDITA ==========

        public async Task<bool> SelezionaOperatoreAsync(int numero)
        {
            // Manuale pag 3: <numero>O
            return await SendCommandAsync($"{numero}O");
        }

        public async Task<bool> VendiRepartoAsync(int reparto, string descrizione, int prezzoCent, decimal qta = 1)
        {
            // Manuale pag 4/17:
            // "PROVA"123H7R = descrizione + prezzo + H + reparto
            // 10*2P = quantità * codice PLU
            
            // FIX BUG-C3: Sanitizza descrizione — le virgolette doppie " rompono il protocollo
            // perché sono usate come delimitatori nel comando. Anche caratteri di controllo
            // (fuori range ASCII 0x20-0x7E) possono causare XOFF o parsing errato.
            var desc = SanitizzaDescrizione(descrizione);
            
            string cmd;
            if (qta != 1)
            {
                cmd = $"\"{desc}\"{qta.ToString(System.Globalization.CultureInfo.InvariantCulture)}*{prezzoCent}H{reparto}R";
            }
            else
            {
                cmd = $"\"{desc}\"{prezzoCent}H{reparto}R";
            }
            
            return await SendCommandAsync(cmd);
        }

        /// <summary>
        /// FIX BUG-C3: Sanitizza la descrizione prodotto per il protocollo 3i.
        /// - Sostituisce virgolette doppie " con apice ' (delimitatore protocollo)
        /// - Rimuove caratteri di controllo fuori range ASCII stampabile (0x20-0x7E)
        /// - Tronca a 38 caratteri (limite 3i)
        /// </summary>
        private static string SanitizzaDescrizione(string descrizione)
        {
            if (string.IsNullOrEmpty(descrizione)) return "ARTICOLO";
            
            // " è il delimitatore della descrizione nel protocollo 3i (pag. 3, sez. 3)
            var desc = descrizione.Replace('"', '\'');
            
            // Manuale 3i pag. 3: "I caratteri trasmessi da PC devono appartenere al range
            // di caratteri corrispondenti ai valori 20H – 7FH"
            // Usiamo 0x20-0x7E (esclude DEL 0x7F che è un control char, non stampabile)
            desc = new string(desc.Where(c => c >= 0x20 && c <= 0x7E).ToArray());
            
            // Fallback se dopo il filtro la descrizione è vuota
            if (string.IsNullOrWhiteSpace(desc)) desc = "ARTICOLO";
            
            // Tronca a 38 caratteri (limite protocollo 3i)
            if (desc.Length > 38) desc = desc.Substring(0, 38);
            
            return desc;
        }

        // ========== CHIUSURA SCONTRINO ==========

        public async Task<bool> ChiudiContantiAsync(int importoCent)
        {
            // Manuale pag 5-6: 1000H1T chiude con €10.00 contanti con resto
            return await SendCommandAsync($"{importoCent}H1T");
        }
        
        public async Task<bool> ChiudiContantiAutoAsync()
        {
            // 1T = chiusura contanti senza importo
            return await SendCommandAsync(ComandiRT.PagamentoContanti);
        }

        public async Task<bool> ChiudiCartaCreditoAsync()
        {
            // Manuale pag 5: 3T = carta credito
            return await SendCommandAsync(ComandiRT.PagamentoCarta);
        }

        /// <summary>
        /// Pagamento misto 3i: due comandi T in sequenza.
        /// Il primo tender (contanti) non copre il totale → scontrino resta aperto.
        /// Il secondo tender (carta) copre il residuo → scontrino si chiude.
        /// Manuale 3i pag 8, esempio 5: 1500H106T + 500H1T (buono + contanti misti).
        /// </summary>
        public async Task<bool> ChiudiMistoAsync(int contantiCent, int cartaCent)
        {
            // Primo tender: contanti (parziale — importo < totale, scontrino resta aperto)
            bool ok1 = await SendCommandAsync($"{contantiCent}H1T");
            if (!ok1)
            {
                LogManager.Error($"ChiudiMisto 3i: primo tender contanti {contantiCent} fallito");
                return false;
            }

            // Secondo tender: carta (chiude scontrino)
            // Se cartaCent > 0: importo esplicito. Se 0: auto-residuo con 3T.
            bool ok2 = cartaCent > 0
                ? await SendCommandAsync($"{cartaCent}H3T")
                : await SendCommandAsync(ComandiRT.PagamentoCarta);

            if (!ok2)
                LogManager.Error($"ChiudiMisto 3i: secondo tender carta {cartaCent} fallito");

            return ok2;
        }

        public async Task<bool> ApriCassettoAsync()
        {
            // Manuale pag 3: a = apertura cassetto
            return await SendCommandAsync(ComandiRT.AperturaCassetto);
        }

        // ========== FUNZIONI FISCALI ==========

        // FIX C6: Restituisce bool per consentire al chiamante di verificare il risultato
        public async Task<bool> ChiusuraFiscaleAsync()
        {
            if (!await SendCommandAsync(ComandiRT.ModoAzzeramenti)) return false;
            await Task.Delay(500);
            if (!await SendCommandAsync(ComandiRT.ChiusuraFiscale)) return false;
            await Task.Delay(500);
            if (!await SendCommandAsync(ComandiRT.ModoVendita)) return false;
            return true;
        }

        public async Task<bool> LetturaGiornalieraAsync()
        {
            if (!await SendCommandAsync(ComandiRT.ModoLetture)) return false;
            await Task.Delay(500);
            if (!await SendCommandAsync(ComandiRT.ChiusuraFiscale)) return false;
            await Task.Delay(500);
            if (!await SendCommandAsync(ComandiRT.ModoVendita)) return false;
            return true;
        }

        public async Task<bool> RapportoRepartiLetturaAsync()
        {
            if (!await SendCommandAsync(ComandiRT.ModoLetture)) return false;
            await Task.Delay(500);
            if (!await SendCommandAsync(ComandiRT.RapportoReparti)) return false;
            await Task.Delay(500);
            if (!await SendCommandAsync(ComandiRT.ModoVendita)) return false;
            return true;
        }

        public async Task<bool> RapportoRepartiAzzeramentoAsync()
        {
            if (!await SendCommandAsync(ComandiRT.ModoAzzeramenti)) return false;
            await Task.Delay(500);
            if (!await SendCommandAsync(ComandiRT.RapportoReparti)) return false;
            await Task.Delay(500);
            if (!await SendCommandAsync(ComandiRT.ModoVendita)) return false;
            return true;
        }

        public async Task<bool> StampaUltimoScontrinoAsync()
        {
            return await SendCommandAsync(ComandiRT.StampaUltimoScontrino);
        }

        public async Task<bool> StampaPenultimoScontrinoAsync()
        {
            return await SendCommandAsync(ComandiRT.StampaPenultimoScontrino);
        }

        public async Task<bool> StampaTerzultimoScontrinoAsync()
        {
            return await SendCommandAsync(ComandiRT.StampaTerzultimoScontrino);
        }

        /// <summary>
        /// Comando 20F: Lettura DGFE per N. azzeramento e scontrino su CARTA.
        /// Stampa una copia dello scontrino dalla memoria fiscale dell'RT.
        /// È una pura lettura — nessun nuovo documento, nessun incasso.
        /// Manuale pag 13: "descrittore"20F
        /// Formato descrittore: 20 caratteri numerici
        ///   <nro_azz_da (5)><nro_azz_a (5)><nro_scontrino_da (5)><nro_scontrino_a (5)>
        /// Es: "00044000440003000030"20F → azzeramento 44, scontrino 30
        /// </summary>
        // FIX BUG-8: aggiunto parametro dataChiusuraZ per uniformità con l'interfaccia.
        // RT3i non usa la data nella copia fiscale (usa il descrittore azz+scontrino),
        // quindi il parametro viene ignorato — la firma è solo per soddisfare IFiscalConnector.
        public async Task<bool> StampaCopiaFiscaleAsync(int azzeramento, int numeroScontrino, DateTime? dataChiusuraZ = null)
        {
            var descrittore = $"{azzeramento:D5}{azzeramento:D5}{numeroScontrino:D5}{numeroScontrino:D5}";
            var cmd = $"\"{descrittore}\"{ComandiRT.StampaCopiaFiscale}";
            return await SendCommandAsync(cmd);
        }

        // ========== STATO REGISTRATORE (FIX #2) ==========

        /// <summary>
        /// Comando 89F: Legge lo stato completo del registratore.
        /// Restituisce 39 byte raw (con NULLs come separatori di campo).
        /// Il manuale 3i specifica:
        ///   Offset  0: Logotipo (2 char + NULL)
        ///   Offset  3: Costruttore (2 char + NULL)
        ///   Offset  6: Matricola (6 char + NULL)
        ///   Offset 13: Stato Fiscale (F/N)
        ///   Offset 14: Stato Scontrino (A=Aperto, C=Chiuso)
        ///   Offset 15: Tipo Scontrino (F/G)
        ///   Offset 16: Num Scontrino Fiscale (6 char)
        ///   ...fino a offset 38
        /// NOTA: la risposta 89F NON ha terminatore CR+LF, quindi NON si può
        /// usare LeggiRispostaAsync che aspetterebbe 10 secondi inutilmente.
        /// </summary>
        public async Task<byte[]> LeggiStatoRegistratoreRawAsync()
        {
            bool inviato = await SendCommandAsync(ComandiRT.StatoRegistratore);
            if (!inviato) return null;

            // La risposta 89F arriva tutta insieme (~39 byte) senza terminatore CR+LF.
            // I dati potrebbero già essere nel pending buffer (letti da ControllaXoffAsync)
            // oppure devono ancora arrivare dallo stream.
            var result = new List<byte>();
            var scadenza = DateTime.Now.AddMilliseconds(2000); // Max 2 sec, non 10

            // FASE 1: Recupera byte dal pending buffer
            if (_pendingBytes != null && _pendingCount > 0)
            {
                for (int i = _pendingOffset; i < _pendingOffset + _pendingCount; i++)
                {
                    byte b = _pendingBytes[i];
                    if (b == XON || b == XOFF || b == NAK || b == ACK) continue;
                    result.Add(b);
                }
                _pendingBytes = null;
                _pendingOffset = 0;
                _pendingCount = 0;
            }

            // FASE 2: Se servono altri byte, leggi dallo stream
            while (result.Count < 39 && DateTime.Now < scadenza)
            {
                try
                {
                    if (_stream.DataAvailable)
                    {
                        int b = _stream.ReadByte();
                        if (b == -1) break;
                        if (b == XON || b == XOFF || b == NAK || b == ACK) continue;
                        result.Add((byte)b);
                    }
                    else
                    {
                        // Se abbiamo già 35+ byte e non arriva più nulla, è completa
                        if (result.Count >= 35) break;
                        await Task.Delay(50);
                    }
                }
                catch (Exception ex) { LogManager.Debug($"LeggiRisposta50F: break ({ex.GetType().Name})"); break; }
            }

            if (result.Count < 15)
            {
                LogManager.Warning($"89F: risposta troppo corta ({result.Count} byte)");
                return null;
            }

            LogManager.Debug($"89F raw: {result.Count} byte, Stato={((char)result[14])}");
            return result.ToArray();
        }

        /// <summary>
        /// Wrapper legacy: restituisce la risposta 89F come stringa (NULLs rimossi).
        /// Usato da altri metodi che leggono matricola, ecc.
        /// </summary>
        public async Task<string> LeggiStatoRegistratoreAsync()
        {
            var raw = await LeggiStatoRegistratoreRawAsync();
            if (raw == null) return null;
            
            // Converte a stringa rimuovendo NULLs e control chars (come faceva prima)
            var sb = new StringBuilder();
            foreach (byte b in raw)
            {
                if (b == 0x00) continue; // Skip NULLs
                if (b < 0x20 && b != CR && b != LF) continue;
                sb.Append((char)b);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Comando 88F: Stato RT per software-house.
        /// Restituisce: matricola#versione#ndgfe#tipo#nazzeramento#npending#@@
        /// </summary>
        public async Task<string> LeggiStatoRTAsync()
        {
            bool inviato = await SendCommandAsync(ComandiRT.StatoFiscale);
            if (!inviato) return null;
            
            return await LeggiRispostaAsync(TIMEOUT_RISPOSTA_MS);
        }

        /// <summary>
        /// Comando 84F: Legge numero azzeramento e numero scontrino corrente dall'RT.
        /// Risposta: "00360027@@" dove 0036=azzeramento, 0027=scontrino
        /// </summary>
        public async Task<(int Azzeramento, int Scontrino)?> LeggiProgressiviRTAsync()
        {
            bool inviato = await SendCommandAsync("84F");
            if (!inviato) return null;
            
            var risposta = await LeggiRispostaAsync(TIMEOUT_RISPOSTA_MS);
            if (string.IsNullOrEmpty(risposta) || risposta.Length < 8)
                return null;
            
            // Formato: "00360027" (4 cifre azzeramento + 4 cifre scontrino)
            if (int.TryParse(risposta.Substring(0, 4), out int azz) &&
                int.TryParse(risposta.Substring(4, 4), out int sco))
            {
                LogManager.Info($"Progressivi RT: Azz={azz}, Scontrino={sco}");
                return (azz, sco);
            }
            
            return null;
        }

        // ========== ANNULLO / RESO ==========

        public async Task<string> VerificaScontrinoAsync(int azzeramento, int numeroScontrino)
        {
            // Comando 50F: verifica se scontrino può essere reso/annullato
            var cmd = $"\"{azzeramento:D4}-{numeroScontrino:D4}\"{ComandiRT.VerificaScontrino}";
            bool inviato = await SendCommandAsync(cmd);
            if (!inviato) return null;
            
            // Attende risposta con aliquote IVA (può richiedere tempo)
            return await LeggiRispostaAsync(TIMEOUT_RISPOSTA_MS);
        }

        /// <summary>
        /// Verifica scontrino con parsing completo della risposta 50F.
        /// Restituisce esito, aliquote IVA e importi per conferma visiva all'operatore.
        /// Manuale pag 20: risposta formato <esito><aliquote IVA 8x xx%><importi IVA 8x nn.nnn,dd>@@
        /// </summary>
        public async Task<Risultato50F> VerificaScontrinoCompletoAsync(int azzeramento, int numeroScontrino, DateTime? dataScontrino = null)
        {
            var raw = await VerificaScontrinoAsync(azzeramento, numeroScontrino);
            
            if (string.IsNullOrEmpty(raw))
                return null;
            
            // Log hex dei primi byte per debug (aiuta a individuare caratteri invisibili)
            var hexDump = string.Join(" ", raw.Take(10).Select(c => $"0x{(int)c:X2}"));
            LogManager.Debug($"50F raw primi 10 byte: [{hexDump}]");
            
            // Difesa: rimuovi eventuali caratteri di controllo iniziali che l'RT potrebbe inviare
            // Il manuale specifica risposte in range ASCII 020H-07AH
            raw = new string(raw.Where(c => c >= 0x20 || c == '\r' || c == '\n').ToArray());
            
            if (string.IsNullOrEmpty(raw))
            {
                LogManager.Error("50F: risposta vuota dopo pulizia caratteri di controllo");
                return null;
            }
            
            var risultato = new Risultato50F
            {
                RispostaRaw = raw
            };
            
            // Parse esito: può essere 1-2 cifre (0-10). Manuale 3i pag 22.
            // FIX BUG-64: Il parser era greedy — leggeva TUTTE le cifre consecutive.
            // Es: risposta "010%22%..." → esito 0 + aliquota 10%, ma il parser leggeva "010" = 10.
            // Strategia: la prima % nella risposta è il terminatore della prima aliquota (2 char + %),
            // quindi posizione_prima_% - 2 = fine dell'esito.
            int esitoLen = 0;
            int primaPercent = raw.IndexOf('%');
            if (primaPercent >= 2)
            {
                // L'aliquota prima del % è sempre 2 caratteri (es: "22", "10", "04", "ES")
                esitoLen = primaPercent - 2;
            }
            else
            {
                // Fallback: se non c'è %, leggi solo cifre (risposte di errore puro senza aliquote)
                while (esitoLen < raw.Length && char.IsDigit(raw[esitoLen])) esitoLen++;
            }
            
            if (esitoLen > 0 && int.TryParse(raw.Substring(0, esitoLen), out int esitoVal))
                risultato.Esito = esitoVal;
            else
            {
                LogManager.Warning($"50F: esito non parsabile da '{raw}' (esitoLen={esitoLen}, primaPercent={primaPercent})");
                risultato.Esito = -1;
            }
            
            risultato.EsitoDescrizione = risultato.Esito switch
            {
                0 => "Scontrino annullabile",
                1 => "Scontrino non esistente",
                2 => "Non è un documento di vendita",
                3 => "Già annullato o reso",
                4 => "Aliquote IVA cambiate",
                5 => "Aliquota IVA non associata a reparto",
                8 => "Già annullato su altro DGFE/RT",
                9 => "Scontrino da altro RT (battute manuali)",
                10 => "Già reso parzialmente (solo reso ammesso)",
                _ => $"Errore sconosciuto ({risultato.Esito})"
            };
            
            if (!risultato.Annullabile && !risultato.Rendibile)
                return risultato;
            
            // Parse aliquote e importi dalla risposta
            // Formato: <esito><8 aliquote xx%><8 importi nn.nnn,dd>
            // Esempio: 022%10%04%ES 00%00%00%00%  100.000,00  50.000,00  0.000,00 ...
            try
            {
                var dati = raw.Substring(esitoLen); // Rimuovi esito (1 o 2 cifre)
                
                // Estrai le 8 aliquote (formato: 2-3 char + %)
                // e i corrispondenti importi (formato italiano: nn.nnn,dd)
                var aliquote = new List<string>();
                var importi = new List<decimal>();
                
                // Strategia: usa regex per trovare tutti gli importi in formato nn.nnn,dd o n.nnn,dd
                var regexImporti = new Regex(
                    @"(\d{1,3}(?:\.\d{3})*),(\d{2})");
                
                var matchImporti = regexImporti.Matches(dati);
                foreach (Match m in matchImporti)
                {
                    // Converti da formato italiano (1.234,56) a decimal
                    var parteIntera = m.Groups[1].Value.Replace(".", "");
                    var parteDecimale = m.Groups[2].Value;
                    if (decimal.TryParse($"{parteIntera}.{parteDecimale}", 
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out decimal val))
                    {
                        importi.Add(val);
                    }
                }
                
                // Estrai le aliquote: cerca pattern tipo "22%", "10%", "04%", "ES", "EN"
                var regexAliq = new Regex(
                    @"(\d{1,2}%|E[SNLRIX])");
                var matchAliq = regexAliq.Matches(dati);
                foreach (Match m in matchAliq)
                {
                    aliquote.Add(m.Value);
                }
                
                // Abbina aliquote e importi (max 8 coppie)
                int numCoppie = Math.Min(aliquote.Count, importi.Count);
                for (int i = 0; i < numCoppie; i++)
                {
                    if (importi[i] > 0)
                    {
                        risultato.Aliquote.Add(new AliquotaIVA
                        {
                            Codice = aliquote[i],
                            ImportoEuro = importi[i]
                        });
                    }
                }
                
                // Totale = somma di tutti gli importi IVA
                risultato.TotaleEuro = importi.Sum();
                
                LogManager.Info($"50F parsed: Totale €{risultato.TotaleEuro:F2}, " +
                    $"{risultato.Aliquote.Count} aliquote attive, raw={raw}");
            }
            catch (Exception ex)
            {
                LogManager.Warning($"50F parse parziale: {ex.Message} (raw={raw})");
                // Anche se il parse fallisce, l'esito è valido
            }
            
            return risultato;
        }

        public async Task<bool> IniziaResoAsync(int azzeramento, int numeroScontrino, DateTime? dataScontrino = null)
        {
            var cmd = $"\"{azzeramento:D4}-{numeroScontrino:D4}\"51F";
            return await SendCommandAsync(cmd);
        }

        /// <summary>
        /// Versione con data per compatibilità interfaccia IFiscalConnector.
        /// Su 3i la data non serve (il comando 52F la ignora).
        /// </summary>
        public Task<bool> AnnullaScontrinoAsync(int azzeramento, int numeroScontrino, DateTime dataScontrino)
        {
            // 3i ignora la data — usa il comando standard
            return AnnullaScontrinoAsync(azzeramento, numeroScontrino);
        }

        public async Task<bool> AnnullaScontrinoAsync(int azzeramento, int numeroScontrino)
        {
            var cmd = $"\"{azzeramento:D4}-{numeroScontrino:D4}\"{ComandiRT.AnnulloScontrino}";
            return await SendCommandAsync(cmd);
        }

        public async Task<bool> AnnullaScontrinoCorrenteAsync()
        {
            // Comando 25F: annulla scontrino corrente (Total Void)
            return await SendCommandAsync("25F");
        }

        // ========== MODIFICATORI / SCONTI ==========

        public async Task<bool> SubtotaleAsync()
        {
            return await SendCommandAsync("=");
        }

        public async Task<bool> ScontoPercentualeRigaAsync(decimal percentuale)
        {
            var perc = percentuale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            return await SendCommandAsync($"{perc}*1M");
        }

        public async Task<bool> ScontoPercentualeTotaleAsync(decimal percentuale)
        {
            // FIX BUG-58: Rimosso prefisso "=" — il caller (InviaScontiRTAsync) chiama
            // sempre SubtotaleAsync() prima, che già invia "=". Con il prefisso, il subtotale
            // veniva stampato DUE VOLTE sullo scontrino (manuale 3i pag 5: "=" è il comando subtotale).
            var perc = percentuale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            return await SendCommandAsync($"{perc}*2M");
        }

        public async Task<bool> ScontoValoreRigaAsync(int importoCent)
        {
            return await SendCommandAsync($"{importoCent}H3M");
        }

        public async Task<bool> ScontovaloreTotaleAsync(int importoCent)
        {
            // FIX BUG-58: Rimosso prefisso "=" — il caller (InviaScontiRTAsync) chiama
            // sempre SubtotaleAsync() prima, che già invia "=". Con il prefisso, il subtotale
            // veniva stampato DUE VOLTE sullo scontrino.
            return await SendCommandAsync($"{importoCent}H4M");
        }

        public async Task<bool> ClearAsync()
        {
            return await SendCommandAsync("K");
        }

        public async Task<bool> StampaMessaggioAsync(string messaggio)
        {
            // FIX BUG-C3: sanitizza anche i messaggi (usano stessa sintassi "testo"@)
            var msg = SanitizzaDescrizione(messaggio);
            return await SendCommandAsync($"\"{msg}\"@");
        }

        // ========== RECOVERY SCONTRINO APERTO (FIX #16) ==========
        
        /// <summary>
        /// Verifica se l'RT ha uno scontrino aperto leggendo lo stato (89F).
        /// Il byte all'offset 14 della risposta raw indica: 'A' = Aperto, 'C' = Chiuso.
        /// </summary>
        public async Task<bool> HaScontrinoApertoAsync()
        {
            try
            {
                var raw = await LeggiStatoRegistratoreRawAsync();
                if (raw == null || raw.Length < 15)
                {
                    LogManager.Warning("89F: risposta troppo corta per determinare stato scontrino");
                    return false; // Non possiamo sapere — assumiamo chiuso
                }
                
                // Manuale 3i: offset 14 = Stato Scontrino (A=Aperto, C=Chiuso)
                char statoScontrino = (char)raw[14];
                LogManager.Debug($"89F stato scontrino: '{statoScontrino}' (A=Aperto, C=Chiuso)");
                return statoScontrino == 'A';
            }
            catch (Exception ex)
            {
                LogManager.Warning($"Errore verifica scontrino aperto: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// FIX #16: Tenta di recuperare l'RT da uno scontrino bloccato.
        /// Prova 3 strategie in sequenza:
        ///   1. K (Clear) — svuota il buffer comandi
        ///   2. 25F (Total Void) — annulla lo scontrino corrente
        ///   3. Disconnetti, riconnetti, riprova 25F — reset connessione TCP
        /// Restituisce true se l'RT è stato recuperato (scontrino chiuso).
        /// </summary>
        public async Task<bool> RecuperaScontrinoApertoAsync()
        {
            LogManager.Info("=== INIZIO RECOVERY SCONTRINO 3i ===");
            
            // Reset flag per poter comunicare
            _xoffRicevuto = false;
            _nakRicevuto = false;
            
            // FASE 1: Verifica PRIMA se c'è uno scontrino aperto (89F)
            // Questo evita di mandare 25F quando non serve → niente "ERRORE NON AMMESSO"
            bool aperto = await HaScontrinoApertoAsync();
            
            if (!aperto)
            {
                LogManager.Info("✅ Nessuno scontrino aperto — stampante già pulita");
                // Torna in modalità vendita per sicurezza
                await SendCommandAsync(ComandiRT.ModoVendita);
                return true;
            }
            
            // FASE 2: Scontrino aperto trovato — prova a chiuderlo
            for (int tentativo = 1; tentativo <= 3; tentativo++)
            {
                try
                {
                    LogManager.Info($"Recovery tentativo {tentativo}/3 — scontrino aperto, tento annullo...");
                    
                    _xoffRicevuto = false;
                    _nakRicevuto = false;
                    
                    // Svuota il buffer comandi con Clear (K)
                    LogManager.Debug("Invio K (Clear)...");
                    await ClearAsync();
                    await Task.Delay(300);
                    
                    // Annullo vero (25F = Total Void)
                    LogManager.Debug("Invio 25F (Total Void)...");
                    bool voidOk = await AnnullaScontrinoCorrenteAsync();
                    await Task.Delay(500);
                    
                    // Verifica se ha funzionato
                    bool ancoraAperto = await HaScontrinoApertoAsync();
                    
                    if (!ancoraAperto)
                    {
                        LogManager.Info($"✅ Recovery RIUSCITO al tentativo {tentativo}");
                        await SendCommandAsync(ComandiRT.ModoVendita);
                        return true;
                    }
                    
                    LogManager.Warning($"Tentativo {tentativo} fallito — scontrino ancora aperto");
                    
                    // Al tentativo 2+: prova a riconnettersi (reset TCP)
                    if (tentativo >= 2)
                    {
                        LogManager.Info("Riconnessione TCP...");
                        Disconnect();
                        await Task.Delay(1000);
                        
                        if (!await ConnectAsync())
                        {
                            LogManager.Error("Riconnessione fallita durante recovery");
                            continue;
                        }
                        
                        await SvuotaStreamAsync();
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Error($"Errore recovery tentativo {tentativo}: {ex.Message}");
                }
            }
            
            LogManager.Error("❌ Recovery FALLITO dopo 3 tentativi — scontrino ancora aperto");
            return false;
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

        // ========== APPENDICE ELIMINACODE BANCO ==========

        /// <summary>
        /// RT3i: invia 40F prima della chiusura per segnalare che ci sarà un'appendice.
        /// </summary>
        public async Task<bool> PreannunciaAppendiceAsync(int eliminacode)
        {
            LogManager.Info($"RT3i: PreannunciaAppendice (40F) per eliminacode {eliminacode}");
            return await SendCommandAsync("40F");
        }

        /// <summary>
        /// RT3i: invia 41F + righe descrittive + 42F dopo la chiusura scontrino.
        /// </summary>
        public async Task<bool> StampaAppendiceAsync(int eliminacode)
        {
            LogManager.Info($"RT3i: StampaAppendice (41F..42F) eliminacode {eliminacode}");

            bool ok1 = await SendCommandAsync("41F");
            if (!ok1) { LogManager.Warning("RT3i: 41F (inizio appendice) rifiutato"); return false; }

            string numStr = eliminacode.ToString("D3");
            string riga1 = "*** BANCO - ELIMINACODE ***";
            string riga2 = $"N. {numStr}";

            await SendCommandAsync($"\"{riga1}\"@");
            await SendCommandAsync($"\"{riga2}\"@");

            bool ok2 = await SendCommandAsync("42F");
            if (!ok2) LogManager.Warning("RT3i: 42F (fine appendice) rifiutato");

            return ok2;
        }

        /// <summary>
        /// RT3i: preannuncia appendice testo libero (40F).
        /// </summary>
        public async Task<bool> PreannunciaAppendiceTestoLiberoAsync(string[] righe)
        {
            LogManager.Info($"RT3i: PreannunciaAppendiceTestoLibero (40F) — {righe?.Length ?? 0} righe");
            return await SendCommandAsync("40F");
        }

        /// <summary>
        /// RT3i: stampa appendice testo libero con 41F + righe + 42F dopo la chiusura.
        /// Le righe vengono troncate a 48 char (larghezza massima scontrino RT3i).
        /// </summary>
        public async Task<bool> StampaAppendiceTestoLiberoAsync(string[] righe)
        {
            if (righe == null || righe.Length == 0) return true;
            LogManager.Info($"RT3i: StampaAppendiceTestoLibero (41F..42F) — {righe.Length} righe");

            bool ok1 = await SendCommandAsync("41F");
            if (!ok1) { LogManager.Warning("RT3i: 41F (inizio appendice testo libero) rifiutato"); return false; }

            foreach (var riga in righe)
            {
                if (string.IsNullOrWhiteSpace(riga)) continue;
                // Tronca a 48 char, esclude virgolette per sicurezza comando
                string r = riga.Replace("\"", "'").Trim();
                if (r.Length > 48) r = r.Substring(0, 48);
                await SendCommandAsync($"\"{r}\"@");
            }

            bool ok2 = await SendCommandAsync("42F");
            if (!ok2) LogManager.Warning("RT3i: 42F (fine appendice testo libero) rifiutato");

            return ok2;
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
