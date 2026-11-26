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
    }
}
