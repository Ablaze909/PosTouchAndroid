using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Stampa;

namespace PosTouch.Core.Sale
{
    /// <summary>
    /// Coda di stampa delle comande (come SpoolerStampa del PC): lavora in sottofondo sulla coda salvata in tavoli.db.
    /// Una stampa che non esce viene ritentata; dopo <see cref="MaxTentativi"/> va "in errore" e l'operatore
    /// viene avvisato; le stampe in errore vengono comunque ritentate ogni 20 secondi.
    /// L'ordine per stampante è rispettato: se la cucina non risponde, le sue comande successive aspettano.
    /// </summary>
    public class SpoolerComande
    {
        /// <summary>Tentativi ravvicinati (dopo 1, 3 e 6 s) prima di dare l'errore: il Wi-Fi che si risveglia non basta a far fallire la comanda.</summary>
        public const int MaxTentativi = 4;
        private static readonly int[] AttesaTentativoMs = { 1000, 3000, 6000 };
        private static readonly TimeSpan IntervalloErrori = TimeSpan.FromSeconds(20);

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

        /// <summary>La rete del locale è tornata: si riprova subito anche ciò che era in errore (senza aspettare il giro).</summary>
        public void ReteTornata()
        {
            _ultimoGiroErrori = DateTime.MinValue;
            lock (_statoBlocco) _nonPrimaDi.Clear();   // rete tornata: niente pause, si riprova subito
            Sveglia();
        }

        public void RiprovaTutto()
        {
            _archivio.RiprovaTutto();
            Sveglia();
        }

        public void RiprovaJob(long id)
        {
            _archivio.RiprovaJob(id);
            Sveglia();
        }

        /// <summary>Stampe ferme: in errore o in attesa dietro una in errore della stessa stampante.</summary>
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
                    _codaVuota = await GiroAsync(anche);
                }
                catch (Exception ex)
                {
                    _codaVuota = false;
                    LogManager.Error($"SPOOLER: errore imprevisto: {ex.Message}");
                }
                // coda vuota: niente letture del database ogni 5 s (una stampa nuova sveglia subito il ciclo)
                await _sveglia.WaitAsync(TimeSpan.FromSeconds(_codaVuota ? 30 : 5));
            }
        }

        private bool _codaVuota;
        /// <summary>Già stampate ma non ancora tolte dalla coda (database non raggiungibile): mai ristampate.</summary>
        private readonly HashSet<long> _giaStampate = new HashSet<long>();
        /// <summary>Stampe in errore da più di tanto (es. dal giorno prima): solo con RIPROVA a mano, mai da sole.</summary>
        private static readonly TimeSpan ScadenzaRiprovaAutomatica = TimeSpan.FromHours(6);

        // ── stato per stampante (chiave IP:porta): invio in corso e attesa prima del prossimo tentativo ──
        private readonly object _statoBlocco = new object();
        private readonly HashSet<string> _inVolo = new HashSet<string>();
        private readonly Dictionary<string, DateTime> _nonPrimaDi = new Dictionary<string, DateTime>();
        private bool _controlloAvvioFatto;

        /// <summary>
        /// true se la coda era vuota. Ogni stampante va per conto suo: il giro manda la prima stampa in attesa di ogni
        /// stampante libera e NON aspetta l'esito (una stampante spenta non rallenta più le altre); a stampa finita
        /// il ciclo si risveglia. L'ordine per stampante resta: finché una stampa è in volo le sue successive aspettano.
        /// </summary>
        private Task<bool> GiroAsync(bool ancheInErrore)
        {
            var coda = _archivio.CodaDaStampare(true);   // sempre anche quelle in errore: servono a tenere l'ordine
            if (coda.Count == 0)
            {
                lock (_statoBlocco) { if (_inVolo.Count == 0) _giaStampate.Clear(); }
                _controlloAvvioFatto = true;
                return Task.FromResult(true);
            }
            bool primoGiro = !_controlloAvvioFatto;
            _controlloAvvioFatto = true;
            var bloccate = new HashSet<string>();
            bool cambiato = false;
            var adesso = DateTime.Now;
            foreach (var job in coda)
            {
                string chiave = $"{job.IP}:{job.Porta}";
                if (bloccate.Contains(chiave)) continue;   // una stampa per stampante, in ordine
                bool giaUscita, occupata;
                lock (_statoBlocco) { giaUscita = _giaStampate.Contains(job.Id); occupata = _inVolo.Contains(chiave); }
                if (occupata) { bloccate.Add(chiave); continue; }
                if (giaUscita) { TogliDallaCoda(job); continue; }
                bool vecchia = adesso - job.Creato > ScadenzaRiprovaAutomatica;
                if (primoGiro && job.Stato != StatoJob.Errore && vecchia)
                {
                    // solo all'avvio: rimasta in coda da ore (app chiusa la sera con la stampante spenta), mai stampata da sola
                    // il giorno dopo. Con RIPROVA/DEVIA dalla coda invece esce, anche se vecchia.
                    _archivio.EsitoJob(job.Id, false, "Stampa di oltre 6 ore fa: non ristampata da sola. Se serve, RIPROVA dalla coda.", true);
                    LogManager.Warning($"SPOOLER ⌛ {job.Descrizione} → {job.Stampante}: troppo vecchia, messa in errore");
                    bloccate.Add(chiave);
                    cambiato = true;
                    continue;
                }
                if (job.Stato == StatoJob.Errore && (!ancheInErrore || vecchia))
                {
                    bloccate.Add(chiave);                   // una stampa in errore più vecchia blocca le successive della stessa stampante
                    continue;
                }
                bloccate.Add(chiave);
                lock (_statoBlocco)
                {
                    // pausa tra un tentativo e l'altro (1, 3, 6 s): non accorciata da altre stampe che svegliano il ciclo
                    if (job.Stato != StatoJob.Errore && _nonPrimaDi.TryGetValue(chiave, out var quando) && adesso < quando) continue;
                    _inVolo.Add(chiave);
                }
                _ = InviaAsync(job, chiave);
            }
            if (cambiato) Cambiato?.Invoke(_archivio.ContaInErrore());
            return Task.FromResult(false);
        }

        /// <summary>Invio di una stampa (in sottofondo) e registrazione dell'esito; alla fine risveglia il ciclo.</summary>
        private async Task InviaAsync(JobStampa job, string chiave)
        {
            try
            {
                string errore;
                try { errore = await AccessoStampanti.InviaAsync(job.IP, job.Porta, job.Dati, TimeSpan.FromSeconds(15), job.ControlloStato); }
                catch (Exception ex) { errore = PosTouch.Core.Diagnostica.ErroriRete.Testo(ex); }

                if (errore == null)
                {
                    lock (_statoBlocco) { _giaStampate.Add(job.Id); _nonPrimaDi.Remove(chiave); }
                    TogliDallaCoda(job);
                    LogManager.Info($"SPOOLER ✅ {job.Descrizione} → {job.Stampante}");
                    if (job.Stato == StatoJob.Errore) Cambiato?.Invoke(_archivio.ContaInErrore());
                    return;
                }
                bool giaInErrore = job.Stato == StatoJob.Errore;
                bool inErrore = giaInErrore || job.Tentativi + 1 >= MaxTentativi;
                _archivio.EsitoJob(job.Id, false, errore, inErrore);
                LogManager.Warning($"SPOOLER ⏳ {job.Descrizione} → {job.Stampante}: {errore} (tentativo {job.Tentativi + 1})");
                if (inErrore && !giaInErrore)
                {
                    job.UltimoErrore = errore;
                    StampaInErrore?.Invoke(job);
                    Cambiato?.Invoke(_archivio.ContaInErrore());
                }
                else if (!inErrore)
                {
                    // nuovo tentativo a breve, ogni volta un po' più in là
                    int attesa = AttesaTentativoMs[Math.Min(job.Tentativi, AttesaTentativoMs.Length - 1)];
                    lock (_statoBlocco) _nonPrimaDi[chiave] = DateTime.Now.AddMilliseconds(attesa);
                    _ = Task.Delay(attesa + 50).ContinueWith(_ => Sveglia());
                }
            }
            catch (Exception ex) { LogManager.Error($"SPOOLER: esito di {job.Descrizione} non registrato: {ex.Message}"); }
            finally
            {
                lock (_statoBlocco) _inVolo.Remove(chiave);
                Sveglia();   // stampante libera: la sua prossima stampa (se c'è) parte subito
            }
        }

        /// <summary>Stampata: si toglie dalla coda; se il database non risponde resta in memoria che è già uscita (niente doppioni).</summary>
        private void TogliDallaCoda(JobStampa job)
        {
            try
            {
                _archivio.EsitoJob(job.Id, true, null, false);
                lock (_statoBlocco) _giaStampate.Remove(job.Id);
            }
            catch (Exception ex) { LogManager.Error($"SPOOLER: {job.Descrizione} stampata ma non tolta dalla coda: {ex.Message}"); }
        }
    }
}
