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
    }
}
