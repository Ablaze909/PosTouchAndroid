using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Stampa;

namespace PosTouch.Core.Sale
{
    /// <summary>
    /// Coda di stampa delle comande (come SpoolerStampa del PC): lavora in sottofondo sulla coda salvata in tavoli.db.
    /// Una stampa che non esce viene ritentata; dopo <see cref="MaxTentativi"/> va "in errore" e l'operatore
    /// viene avvisato; le stampe in errore vengono comunque ritentate ogni 30 secondi.
    /// L'ordine per stampante è rispettato: se la cucina non risponde, le sue comande successive aspettano.
    /// </summary>
    public class SpoolerComande
    {
        public const int MaxTentativi = 2;
        private static readonly TimeSpan IntervalloErrori = TimeSpan.FromSeconds(30);

        private readonly ArchivioTavoli _archivio;
        private readonly SemaphoreSlim _sveglia = new SemaphoreSlim(0);
        private DateTime _ultimoGiroErrori = DateTime.MinValue;
        private int _avviato;

        /// <summary>Una stampa è appena andata in errore (per l'avviso all'operatore).</summary>
        public event Action<JobStampa> StampaInErrore;
        /// <summary>Cambiato il numero di stampe in errore.</summary>
        public event Action<int> Cambiato;

        public SpoolerComande(ArchivioTavoli archivio) { _archivio = archivio; }

        public void Avvia()
        {
            if (Interlocked.Exchange(ref _avviato, 1) == 1) return;
            Task.Run(CicloAsync);
        }

        /// <summary>Da chiamare dopo aver accodato nuove stampe.</summary>
        public void Sveglia() => _sveglia.Release();

        public void RiprovaTutto()
        {
            _archivio.RiprovaTutto();
            Sveglia();
        }

        public int InErrore => _archivio.ContaInErrore();

        public List<DeviazioneStampa> Deviazioni() => _archivio.Deviazioni();

        public void AttivaDeviazione(DeviazioneStampa d) { _archivio.AttivaDeviazione(d); Sveglia(); Cambiato?.Invoke(InErrore); }

        public void RimuoviDeviazioni() { _archivio.RimuoviDeviazioni(); Cambiato?.Invoke(InErrore); }

        public void DeviaJob(long id, string nome, string ip, int porta) { _archivio.DeviaJob(id, nome, ip, porta); Sveglia(); Cambiato?.Invoke(InErrore); }

        private async Task CicloAsync()
        {
            while (true)
            {
                try
                {
                    bool anche = DateTime.Now - _ultimoGiroErrori >= IntervalloErrori;
                    if (anche) _ultimoGiroErrori = DateTime.Now;
                    await GiroAsync(anche);
                }
                catch (Exception ex)
                {
                    LogManager.Error($"SPOOLER: errore imprevisto: {ex.Message}");
                }
                await _sveglia.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        private async Task GiroAsync(bool ancheInErrore)
        {
            var coda = _archivio.CodaDaStampare(true);   // sempre anche quelle in errore: servono a tenere l'ordine
            if (coda.Count == 0) return;
            var bloccate = new HashSet<string>();
            bool cambiato = false;
            foreach (var job in coda)
            {
                string chiave = $"{job.IP}:{job.Porta}";
                if (bloccate.Contains(chiave)) continue;   // stampante giù: le successive aspettano, in ordine
                if (job.Stato == StatoJob.Errore && !ancheInErrore)
                {
                    bloccate.Add(chiave);                   // una stampa in errore più vecchia blocca le successive della stessa stampante
                    continue;
                }

                string errore = await AccessoStampanti.InviaAsync(job.IP, job.Porta, job.Dati, TimeSpan.FromSeconds(15), job.ControlloStato);
                if (errore == null)
                {
                    _archivio.EsitoJob(job.Id, true, null, false);
                    LogManager.Info($"SPOOLER ✅ {job.Descrizione} → {job.Stampante}");
                    cambiato |= job.Stato == StatoJob.Errore;
                    continue;
                }

                bloccate.Add(chiave);
                bool giaInErrore = job.Stato == StatoJob.Errore;
                bool inErrore = giaInErrore || job.Tentativi + 1 >= MaxTentativi;
                _archivio.EsitoJob(job.Id, false, errore, inErrore);
                LogManager.Warning($"SPOOLER ⏳ {job.Descrizione} → {job.Stampante}: {errore} (tentativo {job.Tentativi + 1})");
                if (inErrore && !giaInErrore)
                {
                    cambiato = true;
                    job.UltimoErrore = errore;
                    StampaInErrore?.Invoke(job);
                }
                else if (!inErrore)
                {
                    // secondo tentativo a breve
                    _ = Task.Delay(2000).ContinueWith(_ => Sveglia());
                }
            }
            if (cambiato) Cambiato?.Invoke(_archivio.ContaInErrore());
        }
    }
}
