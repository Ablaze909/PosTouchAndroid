using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;
using PosTouch.Core.Stampa;
using PosTouch.Core.Vendita;

namespace PosTouch.Core.Sale
{
    /// <summary>
    /// Un tavolo aperto sullo schermo. Logica copiata da VenditaContoClienteDialog del PC:
    /// ⬅ SALA = salva + comanda automatica delle righe nuove + storni; 🚫 NO STAMPA = segna comandate senza stampare;
    /// ⏩ AVANZA = VAI portata; 📋 PRECONTO; chiusura dopo il pagamento.
    /// La memoria è la fonte autorevole: lo stato viene salvato tutto insieme alle stampe da accodare.
    /// </summary>
    public partial class SessioneTavolo
    {
        private readonly ArchivioTavoli _archivio;
        private readonly SpoolerComande _spooler;
        private readonly Func<Impostazioni> _impostazioni;
        private readonly List<RigaScontrino> _storniPendenti = new List<RigaScontrino>();

        public TavoloAperto Tavolo { get; }
        public Sala Sala { get; }
        public Carrello Carrello { get; } = new Carrello();
        public bool IsContoCliente => Tavolo.SalaId == ConfigSale.SalaConti;
        public string NomeTavolo => IsContoCliente ? (string.IsNullOrWhiteSpace(Tavolo.NomeCliente) ? $"CONTO {Tavolo.Numero}" : Tavolo.NomeCliente) : $"TAVOLO {Tavolo.Numero}";
        public string NomeSala => IsContoCliente ? "Conti sospesi" : Sala?.Nome ?? "";
        public string Titolo => $"{NomeSala} — {NomeTavolo}";
        public bool HaStorniPendenti => _storniPendenti.Count > 0;

        private ConfigSale Cfg => _impostazioni().Sale;
        /// <summary>Opzioni di stampa (uscite e stampanti per sala, come POSBY).</summary>
        private ConfigStampe Stampe => _impostazioni().Stampe ?? new ConfigStampe();

        public SessioneTavolo(ArchivioTavoli archivio, SpoolerComande spooler, Func<Impostazioni> impostazioni, int salaId, int numero, string nomeCliente = null)
        {
            _archivio = archivio;
            _spooler = spooler;
            _impostazioni = impostazioni;
            Sala = Cfg.SalaDa(salaId);
            Tavolo = _archivio.ApriTavolo(salaId, numero, nomeCliente);
            Carrello.SeparatoriPortate = Cfg.SeparatoriPortate;
            // listino della sala (o dei conti clienti): vale per gli articoli che si battono da ora; le righe salvate restano
            Carrello.Listino = IsContoCliente ? Cfg.ListinoContiClienti : Sala?.Listino ?? 1;
            Carrello.Carica(_archivio.Righe(Tavolo.Id));
            _storniPendenti.AddRange(_archivio.StorniPendenti(Tavolo.Id));   // storni rimasti da una sessione interrotta
            Carrello.ComandateRimosse += r => _storniPendenti.AddRange(r.Select(x => x.Copia()));
            // ResetPrecontoSeAttivo: il giallo si toglie solo se il conto cambia davvero (non scegliendo una portata)
            Carrello.Cambiato += () => { if (Tavolo.Preconto && FirmaConto() != _firmaPreconto) Tavolo.Preconto = false; };
            _firmaPreconto = FirmaConto();
        }

        public bool Nuovo => !Carrello.HaArticoli;

        private string _firmaPreconto;
        private string FirmaConto() => $"{Carrello.TotaleCent}|{string.Join(",", Carrello.Righe.Where(r => r.Tipo == TipoRiga.Prodotto).Select(r => $"{r.ProdottoId}x{r.Quantita}"))}";

        /// <summary>Salvataggio intermedio (dopo ogni modifica): nessuna stampa, il tavolo resta aperto anche se vuoto, storni pendenti al sicuro.</summary>
        public void SalvaBozza()
        {
            if (_chiusa) return;
            _archivio.Salva(Tavolo, Carrello.Righe.ToList(), storniPendenti: _storniPendenti);
        }

        private bool _chiusa;

        /// <summary>Esegue un'uscita; se il salvataggio fallisce rimette i flag "comandata" e la portata com'erano (si può riprovare).</summary>
        private EsitoTavolo ConRipristino(Func<EsitoTavolo> azione)
        {
            var comandate = Carrello.Righe.Where(r => r.Tipo == TipoRiga.Prodotto).ToDictionary(r => r, r => r.Comandata);
            int portata = Tavolo.PortataCorrente;
            var (ordine, vai, finePasto) = (Tavolo.UltimoOrdine, Tavolo.UltimoVai, Tavolo.FinePasto);
            try { return azione(); }
            catch
            {
                foreach (var kv in comandate) kv.Key.Comandata = kv.Value;
                Tavolo.PortataCorrente = portata;
                (Tavolo.UltimoOrdine, Tavolo.UltimoVai, Tavolo.FinePasto) = (ordine, vai, finePasto);
                throw;
            }
        }

        // ══════════════ USCITE DAL TAVOLO ══════════════

        /// <summary>⬅ SALA (btnTornaSala_Click): comanda automatica delle righe nuove + storni accumulati.</summary>
        public EsitoTavolo EsciConComanda() => Esci(stampaComande: true, stampaStorni: true);

        /// <summary>🚫 NO STAMPA: righe segnate come comandate senza stampa; storni stampati solo se l'impostazione lo prevede.</summary>
        public EsitoTavolo EsciNoStampa() => Esci(stampaComande: false, stampaStorni: Cfg.StampaStorniConNoStampa);

        /// <summary>✖ ANNULLA: salva senza inviare le comande (gli storni partono comunque, per non lasciare la cucina all'oscuro).</summary>
        public EsitoTavolo EsciSenzaInvio()
        {
            var esito = new EsitoTavolo();
            var (stampe, storni) = PreparaStorni(stampaStorni: true, esito);
            Carrello.RiordinaPortate();
            _archivio.Salva(Tavolo, Carrello.Righe.ToList(), stampe, storni, liberaSeVuoto: true);
            _chiusa = true;   // uscita salvata: nessun salvataggio successivo può riaprire il tavolo
            _storniPendenti.Clear();
            Sveglia(stampe);
            esito.StorniAccodati = stampe.Count;
            return esito;
        }

        /// <summary>
        /// Tavolo pagato (scontrino o conto): si libera. Gli articoli venduti non ancora comandati partono in comanda
        /// (pagato subito al tavolo: la cucina deve comunque prepararli) e gli storni pendenti vengono stampati.
        /// </summary>
        /// <param name="venduti">Righe del documento emesso (il carrello a video è già stato svuotato).</param>
        public EsitoTavolo ChiudiPagato(IList<RigaScontrino> venduti = null)
        {
            var esito = new EsitoTavolo();
            var stampe = new List<JobStampa>();
            if (venduti != null && venduti.Any(r => r.Tipo == TipoRiga.Prodotto && !r.Comandata))
            {
                // la comanda si prepara sulle righe vendute (copie: il carrello a video non si tocca), poi il tavolo si salva vuoto
                if (Cfg.AbilitaComande) stampe.AddRange(PreparaComande(true, esito, venduti));
            }
            var (stampeStorno, storni) = PreparaStorni(stampaStorni: true, esito);
            stampe.AddRange(stampeStorno);
            _archivio.Salva(Tavolo, new List<RigaScontrino>(), stampe, storni, liberaSeVuoto: true);
            _chiusa = true;   // uscita salvata: nessun salvataggio successivo può riaprire il tavolo
            _storniPendenti.Clear();
            Sveglia(stampe);
            return esito;
        }

        private EsitoTavolo Esci(bool stampaComande, bool stampaStorni) => ConRipristino(() => EsciInterno(stampaComande, stampaStorni));

        private EsitoTavolo EsciInterno(bool stampaComande, bool stampaStorni)
        {
            var esito = new EsitoTavolo();
            Carrello.RiordinaPortate();   // prima della comanda: la cucina riceve le portate in ordine (SEGUE prima di SEGUE 2)
            var stampe = new List<JobStampa>();
            // comande attive: si prepara sempre (anche senza uscite: gli articoli restano da comandare con avviso, regola 12)
            if (Cfg.AbilitaComande)
                stampe.AddRange(PreparaComande(stampaComande, esito));
            else
                foreach (var r in Carrello.Righe.Where(r => r.Tipo == TipoRiga.Prodotto)) r.Comandata = true;

            var (stampeStorno, storni) = PreparaStorni(stampaStorni, esito);
            stampe.AddRange(stampeStorno);
            _archivio.Salva(Tavolo, Carrello.Righe.ToList(), stampe, storni, liberaSeVuoto: true);
            _chiusa = true;   // uscita salvata: nessun salvataggio successivo può riaprire il tavolo   // tutto insieme: niente ristampe, niente perdite
            _storniPendenti.Clear();
            Sveglia(stampe);
            esito.StorniAccodati = stampeStorno.Count;
            return esito;
        }

        // ══════════════ interni ══════════════

        /// <summary>Coperti del tavolo (per la banda "dove sono" della vendita).</summary>
        public int NumeroCoperti => Coperti();

        /// <summary>Coperti = quantità della riga "coperto" della sala.</summary>
        private int Coperti(IEnumerable<RigaScontrino> righe = null)
        {
            int id = Sala?.CopertoProdottoId ?? 0;
            if (id <= 0) return 0;
            return (int)(righe ?? Carrello.Righe).Where(r => r.Tipo == TipoRiga.Prodotto && r.ProdottoId == id).Sum(r => r.Quantita);
        }

        private void Sveglia(List<JobStampa> stampe) { if (stampe.Count > 0) _spooler.Sveglia(); }
    }
}
