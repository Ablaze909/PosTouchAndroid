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
            // un testo che inizia con una cifra ("1/2 CAPRICCIOSA") usciva senza la prima cifra ("/2 ..."): spazio davanti
            if (text.Length > 0 && char.IsDigit(text[0])) text = " " + text;
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
    }
}
