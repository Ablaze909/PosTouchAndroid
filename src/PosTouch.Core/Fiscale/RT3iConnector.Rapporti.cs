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
    }
}
