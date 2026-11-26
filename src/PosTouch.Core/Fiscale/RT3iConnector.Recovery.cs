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
        /// <summary>
        /// Dopo una chiusura non confermata: NAK/XOFF arrivati durante l'attesa del footer (carta, coperchio) bloccherebbero
        /// il 89F e la risposta mancante sembrerebbe "chiuso". Si azzerano i flag e si distingue lo stato non letto.
        /// </summary>
        public async Task<bool?> StatoScontrinoDopoChiusuraAsync()
        {
            _xoffRicevuto = false;
            _nakRicevuto = false;
            try
            {
                var raw = await LeggiStatoRegistratoreRawAsync();
                if (raw == null || raw.Length < 15) { LogManager.Warning("89F dopo chiusura: stato non leggibile"); return null; }
                char stato = (char)raw[14];
                LogManager.Info($"89F dopo chiusura: '{stato}'");
                return stato == 'A' ? true : stato == 'C' ? false : (bool?)null;
            }
            catch (Exception ex) { LogManager.Warning($"89F dopo chiusura: {ex.Message}"); return null; }
        }

        public async Task<bool> RecuperaScontrinoApertoAsync()
        {
            LogManager.Info("=== INIZIO RECOVERY SCONTRINO 3i ===");
            
            // Reset flag per poter comunicare
            _xoffRicevuto = false;
            _nakRicevuto = false;
            
            // FASE 1: Verifica PRIMA se c'è uno scontrino aperto (89F)
            // Questo evita di mandare 25F quando non serve → niente "ERRORE NON AMMESSO"
            // Stato non leggibile: NON si dà per pulito (le righe nuove finirebbero nello scontrino rimasto aperto)
            bool? stato = await StatoScontrinoDopoChiusuraAsync();
            if (stato == null)
            {
                UltimoErrore = "Stato del registratore non leggibile";
                LogManager.Error("Recovery 3i: stato scontrino non leggibile — vendita bloccata finché l'RT non risponde");
                return false;
            }
            bool aperto = stato.Value;
            
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
    }
}
