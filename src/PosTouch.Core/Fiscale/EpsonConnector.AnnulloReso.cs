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
    }
}
