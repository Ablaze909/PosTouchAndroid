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
                    // solo il formato RT vero (AAMMGGHHMM + tipo + 5 cifre): un'altra risposta rimasta in coda
                    // non deve mai valere come "scontrino emesso"
                    if (risposta.Length >= 16 && System.Text.RegularExpressions.Regex.IsMatch(risposta, @"^\d{10}[A-Z]\d{5}"))
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
            return await NonCriticoAsync($"{numero}O");
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

        /// <summary>
        /// Prima del pagamento si butta ogni risposta vecchia rimasta in attesa (es. un 89F arrivato in ritardo):
        /// altrimenti la lettura della conferma (TX_FOOTER) potrebbe prenderla per la conferma dello scontrino.
        /// </summary>
        private async Task PrimaDellaChiusuraAsync()
        {
            if (_stream == null) return;
            await SvuotaStreamAsync();
        }

        public async Task<bool> ChiudiContantiAsync(int importoCent)
        {
            // Manuale pag 5-6: 1000H1T chiude con €10.00 contanti con resto
            await PrimaDellaChiusuraAsync();
            return await SendCommandAsync($"{importoCent}H1T");
        }
        
        public async Task<bool> ChiudiContantiAutoAsync()
        {
            // 1T = chiusura contanti senza importo
            await PrimaDellaChiusuraAsync();
            return await SendCommandAsync(ComandiRT.PagamentoContanti);
        }

        public async Task<bool> ChiudiCartaCreditoAsync()
        {
            // Manuale pag 5: 3T = carta credito
            await PrimaDellaChiusuraAsync();
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
            await PrimaDellaChiusuraAsync();
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
            // un messaggio che inizia con una cifra ("1/2 CAPRICCIOSA") usciva senza la prima cifra ("/2 ..."):
            // uno spazio davanti la protegge
            if (msg.Length > 0 && char.IsDigit(msg[0])) msg = (" " + msg).Length > 38 ? " " + msg.Substring(0, 37) : " " + msg;
            return await NonCriticoAsync($"\"{msg}\"@");
        }

        /// <summary>
        /// Comando che può essere rifiutato senza compromettere lo scontrino (operatore, messaggi, preannuncio appendice):
        /// se il 3i risponde XOFF si sblocca subito con Clear (K). Prima l'XOFF restava "acceso" e il comando dopo
        /// aspettava 10 s un XON che non arrivava, mandando la vendita in errore anche se la chiusura non era mai partita.
        /// Un NAK (carta, coperchio) invece resta: serve l'intervento fisico.
        /// </summary>
        private async Task<bool> NonCriticoAsync(string comando)
        {
            bool ok = await SendCommandAsync(comando);
            if (!ok && !_nakRicevuto)
            {
                LogManager.Warning($"RT3i: comando non essenziale rifiutato ({comando}): sblocco con Clear");
                _xoffRicevuto = false;
                await SendCommandAsync("K");
                _xoffRicevuto = false;
            }
            return ok;
        }

        // ========== APPENDICE ELIMINACODE BANCO ==========

        /// <summary>
        /// RT3i: invia 40F prima della chiusura per segnalare che ci sarà un'appendice.
        /// </summary>
        public async Task<bool> PreannunciaAppendiceAsync(int eliminacode)
        {
            LogManager.Info($"RT3i: PreannunciaAppendice (40F) per eliminacode {eliminacode}");
            return await NonCriticoAsync("40F");
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
            return await NonCriticoAsync("40F");
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
    }
}
