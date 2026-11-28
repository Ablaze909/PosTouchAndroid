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
    public partial class RT3iConnector
    {
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
                    // risposta arrivata a pezzi: si accoda a quella non ancora letta (prima la sovrascriveva e se ne perdeva un pezzo)
                    if (_pendingBytes != null && _pendingCount > 0)
                    {
                        var unito = new byte[_pendingCount + datiCount];
                        Array.Copy(_pendingBytes, _pendingOffset, unito, 0, _pendingCount);
                        Array.Copy(datiByte, 0, unito, _pendingCount, datiCount);
                        datiByte = unito;
                        datiCount = unito.Length;
                    }
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
    }
}
