using System;
using System.Threading.Tasks;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Fiscale;

namespace PosTouch.Core.Cassa
{
    public class EsitoOperazione
    {
        public bool Ok { get; set; }
        public string Messaggio { get; set; } = "";
    }

    /// <summary>Operazioni di servizio sul registratore (chiusura, letture, ristampe, sblocco).</summary>
    public class FunzioniCassa
    {
        private readonly Func<Impostazioni> _impostazioni;

        public FunzioniCassa(Func<Impostazioni> impostazioni)
        {
            _impostazioni = impostazioni;
        }

        public Task<EsitoOperazione> ChiusuraFiscaleAsync() => Esegui("Chiusura fiscale", rt => rt.ChiusuraFiscaleAsync());
        public Task<EsitoOperazione> LetturaGiornalieraAsync() => Esegui("Lettura giornaliera", rt => rt.LetturaGiornalieraAsync());
        public Task<EsitoOperazione> RapportoRepartiAsync() => Esegui("Rapporto reparti", rt => rt.RapportoRepartiLetturaAsync());
        public Task<EsitoOperazione> RistampaPenultimoAsync() => Esegui("Ristampa penultimo scontrino", rt => rt.StampaPenultimoScontrinoAsync());
        public Task<EsitoOperazione> RistampaUltimoAsync() => Esegui("Ristampa ultimo scontrino", rt => rt.StampaUltimoScontrinoAsync());
        public Task<EsitoOperazione> ApriCassettoAsync() => Esegui("Apertura cassetto", rt => rt.ApriCassettoAsync());
        public Task<EsitoOperazione> SbloccaRTAsync() => Esegui("Sblocco RT", rt => rt.RecuperaScontrinoApertoAsync());

        /// <summary>Prova la connessione e legge lo stato del registratore.</summary>
        public async Task<EsitoOperazione> TestConnessioneAsync()
        {
            if (!await AccessoRT.PrendiAsync(TimeSpan.FromSeconds(3)))
                return new EsitoOperazione { Messaggio = AccessoRT.MessaggioOccupato };
            try { return await TestConnessioneInterno(); }
            finally { AccessoRT.Rilascia(); }
        }

        private async Task<EsitoOperazione> TestConnessioneInterno()
        {
            var cfg = _impostazioni();
            try
            {
                using var rt = FiscalConnectorFactory.Create(cfg.TipoCassa, cfg.IP_RT, cfg.Porta_RT);
                if (!await rt.ConnectAsync())
                    return new EsitoOperazione { Messaggio = $"Registratore non raggiungibile ({cfg.IP_RT}:{cfg.Porta_RT}).\n{rt.UltimoErrore}" };
                string stato = await rt.LeggiStatoRTAsync();
                var prog = await rt.LeggiProgressiviRTAsync();
                rt.Disconnect();
                return new EsitoOperazione
                {
                    Ok = true,
                    Messaggio = $"Collegato a {FiscalConnectorFactory.NomeTipo(cfg.TipoCassa)}\n{cfg.IP_RT}:{cfg.Porta_RT}\n\n" +
                                (string.IsNullOrWhiteSpace(stato) ? "" : $"Stato: {stato}\n") +
                                (prog.HasValue ? $"Azzeramento: {prog.Value.Azzeramento}  Scontrino: {prog.Value.Scontrino}" : "")
                };
            }
            catch (Exception ex)
            {
                LogManager.Error($"Test connessione: {ex.Message}");
                return new EsitoOperazione { Messaggio = PosTouch.Core.Diagnostica.ErroriRete.Testo(ex) };
            }
        }

        private async Task<EsitoOperazione> Esegui(string nome, Func<IFiscalConnector, Task<bool>> azione)
        {
            if (!await AccessoRT.PrendiAsync(TimeSpan.FromSeconds(3)))
                return new EsitoOperazione { Messaggio = AccessoRT.MessaggioOccupato };
            try { return await EseguiInterno(nome, azione); }
            finally { AccessoRT.Rilascia(); }
        }

        private async Task<EsitoOperazione> EseguiInterno(string nome, Func<IFiscalConnector, Task<bool>> azione)
        {
            var cfg = _impostazioni();
            try
            {
                using var rt = FiscalConnectorFactory.Create(cfg.TipoCassa, cfg.IP_RT, cfg.Porta_RT);
                if (!await rt.ConnectAsync())
                    return new EsitoOperazione { Messaggio = $"Registratore non raggiungibile ({cfg.IP_RT}:{cfg.Porta_RT})." };

                bool ok = await azione(rt);
                string err = rt.UltimoErrore;
                rt.Disconnect();
                LogManager.Info($"{nome}: {(ok ? "OK" : "FALLITA " + err)}");
                return new EsitoOperazione
                {
                    Ok = ok,
                    Messaggio = ok ? $"{nome} eseguita." : $"{nome} non riuscita.\n{err}"
                };
            }
            catch (Exception ex)
            {
                LogManager.Error($"{nome}: {ex.Message}");
                return new EsitoOperazione { Messaggio = $"{nome}: {PosTouch.Core.Diagnostica.ErroriRete.Testo(ex)}" };
            }
        }
    }
}
