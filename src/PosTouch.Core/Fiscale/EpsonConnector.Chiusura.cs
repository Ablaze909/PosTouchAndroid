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

        /// <summary>
        /// Esito di 1-084 finale. Codice 2 (carta in esaurimento) è solo un avviso: lo scontrino è chiuso,
        /// come per OkAsync (prima diventava "incerto" e si chiedeva all'operatore senza motivo).
        /// </summary>
        private bool EsitoChiusura(EpsonResponse resp)
        {
            if (resp.Success) { ParseChiusura(resp); return true; }
            if (resp.IsError && resp.ErrorCode == 2)
            {
                LogManager.Warning("Epson: chiusura con avviso carta in esaurimento");
                UltimoErrore = "Attenzione: carta in esaurimento";
                try { ParseChiusura(resp); } catch { }
                return true;
            }
            UltimoErrore = resp.IsError
                ? $"Errore stampante: {resp.ErrorMessage} (cod. {resp.ErrorCode})"
                : $"Errore comunicazione: {resp.ErrorMessage}";
            return false;
        }

        public async Task<bool> ChiudiContantiAsync(int importoCent)
        {
            string descr = "CONTANTI";
            string data = Op2 + descr           // OP(2) + DESCR(8)
                        + Amt9(importoCent)      // AMN(9)
                        + "0"                    // TYPE: 0 = Cash
                        + "01"                   // IND: 01
                        + "1";                   // L/R: 1

            var resp = await SendAsync(1, 84, data, TIMEOUT_PRINT);
            return EsitoChiusura(resp);
        }

        public async Task<bool> ChiudiContantiAutoAsync()
        {
            // AMN=000000000 → pagamento automatico importo esatto
            string descr = "CONTANTI";
            string data = Op2 + descr + Amt9(0) + "0" + "01" + "1";
            var resp = await SendAsync(1, 84, data, TIMEOUT_PRINT);
            return EsitoChiusura(resp);
        }

        public async Task<bool> ChiudiCartaCreditoAsync()
        {
            string descr = "CARTA";
            string data = Op2 + descr + Amt9(0) + "2" + "01" + "1";
            //                                     TYPE=2 Credit Card
            var resp = await SendAsync(1, 84, data, TIMEOUT_PRINT);
            return EsitoChiusura(resp);
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
            var resp1 = await SendAsync(1, 84, data1, TIMEOUT_PRINT);
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
            var resp2 = await SendAsync(1, 84, data2, TIMEOUT_PRINT);
            bool ok = EsitoChiusura(resp2);   // solo qui il formato completo con CHG+DATE+TIME+FRN
            if (!ok) LogManager.Error($"ChiudiMisto Epson: secondo tender carta fallito — {UltimoErrore}");
            return ok;
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
    }
}
