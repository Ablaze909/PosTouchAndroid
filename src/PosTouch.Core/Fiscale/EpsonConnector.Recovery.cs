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
    }
}
