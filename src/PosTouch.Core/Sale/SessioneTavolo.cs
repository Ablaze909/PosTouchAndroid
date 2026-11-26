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
    /// <summary>Risultato di un'uscita dal tavolo (avvisi da mostrare all'operatore, vuoto = tutto ok).</summary>
    public class EsitoTavolo
    {
        public List<string> Avvisi { get; } = new List<string>();
        public int ComandeAccodate { get; set; }
        public int StorniAccodati { get; set; }
    }

    /// <summary>Anteprima di ⏩ AVANZA (VAI portata) da confermare.</summary>
    public class AnteprimaAvanza
    {
        public bool Possibile { get; set; }
        public string Titolo { get; set; } = "";
        public string Messaggio { get; set; } = "";
        public int Portata { get; set; }
        public string NomeSeparatore { get; set; } = "";
        /// <summary>true = portata senza articoli da stampare: si avanza e basta.</summary>
        public bool SenzaStampa { get; set; }
    }

    /// <summary>
    /// Un tavolo aperto sullo schermo. Logica copiata da VenditaContoClienteDialog del PC:
    /// ⬅ SALA = salva + comanda automatica delle righe nuove + storni; 🚫 NO STAMPA = segna comandate senza stampare;
    /// ⏩ AVANZA = VAI portata; 📋 PRECONTO; chiusura dopo il pagamento.
    /// La memoria è la fonte autorevole: lo stato viene salvato tutto insieme alle stampe da accodare.
    /// </summary>
    public class SessioneTavolo
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
            try { return azione(); }
            catch
            {
                foreach (var kv in comandate) kv.Key.Comandata = kv.Value;
                Tavolo.PortataCorrente = portata;
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

        // ══════════════ COMANDA (InviaComandaAutomaticaAsync del PC) ══════════════

        private List<JobStampa> PreparaComande(bool stampa, EsitoTavolo esito, IList<RigaScontrino> righe = null)
        {
            righe ??= Carrello.Righe;
            var stampe = new List<JobStampa>();
            bool primaComanda = !righe.Any(r => r.Tipo == TipoRiga.Prodotto && r.Comandata);
            var nuove = righe.Where(r => r.Tipo == TipoRiga.Prodotto && !r.Comandata && r.CentroProduzioneId > 0).ToList();
            var senzaCentro = righe.Where(r => r.Tipo == TipoRiga.Prodotto && !r.Comandata && r.CentroProduzioneId <= 0).ToList();

            if (nuove.Count == 0 || !stampa)
            {
                foreach (var r in nuove.Concat(senzaCentro)) r.Comandata = true;
                if (nuove.Count > 0) LogManager.Info($"NO STAMPA {Titolo}: {nuove.Count} righe segnate come comandate");
                return stampe;
            }

            int progressivo = _archivio.ProssimoProgressivoComanda();
            int coperti = Coperti();
            List<RigaScontrino> flusso = Cfg.AbilitaPortate ? FlussoPortate(righe) : null;
            var gruppi = nuove.GroupBy(r => r.CentroProduzioneId).ToList();
            var nonInviate = new List<RigaScontrino>();

            foreach (var g in gruppi)
            {
                var st = InstradamentoStampa.Risolvi(Stampe, Tavolo.SalaId, g.Key);
                string nomeCentro = Stampe.NomeUscita(g.Key);
                if (st == null)
                {
                    // righe lasciate "da comandare": partiranno quando la stampante sarà impostata
                    esito.Avvisi.Add($"{nomeCentro}: nessuna stampante impostata, comanda NON stampata ({g.Sum(r => r.Quantita):0.###} pz).\nGli articoli restano da comandare.");
                    LogManager.Warning($"COMANDA {Titolo}: centro {nomeCentro} senza stampante");
                    nonInviate.AddRange(g);
                    continue;
                }
                // RIORDINO per centro: solo se QUESTO centro ha già ricevuto articoli della stessa portata
                bool riordino = !primaComanda && g.Any(r => righe.Any(prev => prev != r && prev.Tipo == TipoRiga.Prodotto && prev.Comandata
                                                                       && prev.CentroProduzioneId == g.Key && prev.Portata == r.Portata));
                Dictionary<string, List<RigaScontrino>> altri = null;
                if (Cfg.StampaRiepilogoAltriCentri && !Cfg.AbilitaPortate)
                    altri = gruppi.Where(x => x.Key != g.Key)
                                  .ToDictionary(x => Stampe.NomeUscita(x.Key), x => x.ToList());

                var dati = new DatiComanda
                {
                    NomeCentro = nomeCentro, CentroId = g.Key, NomeTavolo = NomeTavolo, NomeSala = NomeSala,
                    Progressivo = progressivo, Coperti = coperti, Riordino = riordino,
                    Righe = g.ToList(), AltriCentri = altri, FlussoPortate = flusso
                };
                stampe.Add(new JobStampa
                {
                    Tipo = TipoJob.Comanda,
                    Descrizione = $"{(riordino ? "RIORDINO" : "COMANDA")} {Titolo} → {nomeCentro}",
                    Stampante = nomeCentro, IP = st.IP, Porta = st.Porta, ControlloStato = st.ControlloStato,
                    Dati = Finitura.Applica(LayoutComanda.Comanda(dati, Cfg.StileComandaProdotto, Cfg.StileComandaSeparatore, 0, Cfg.AccorpaStampaComanda, Cfg.StileComandaVariante),
                                            st, Cfg.CicalinoComande)
                });
            }

            foreach (var r in nuove.Concat(senzaCentro).Except(nonInviate)) r.Comandata = true;
            esito.ComandeAccodate = stampe.Count;
            LogManager.Info($"COMANDA {Titolo}: {nuove.Count - nonInviate.Count} righe, {stampe.Count} stampe in coda (Com.{progressivo})");
            return stampe;
        }

        /// <summary>
        /// Flusso con le portate per la stampa (come il PC): righe nuove con centro + separatori seguiti
        /// da articoli della stessa portata; SUBITO in testa.
        /// </summary>
        private static List<RigaScontrino> FlussoPortate(IList<RigaScontrino> righe)
        {
            var grezzo = righe.Where(r => (r.Tipo == TipoRiga.Prodotto && !r.Comandata && r.CentroProduzioneId > 0) || r.Tipo == TipoRiga.Portata).ToList();
            var ris = new List<RigaScontrino>();
            for (int i = 0; i < grezzo.Count; i++)
            {
                if (grezzo[i].Tipo == TipoRiga.Portata)
                {
                    int portata = grezzo[i].Portata;
                    bool haProdotti = false;
                    for (int j = i + 1; j < grezzo.Count && grezzo[j].Tipo != TipoRiga.Portata; j++)
                        if (grezzo[j].Portata == portata) { haProdotti = true; break; }
                    if (haProdotti) ris.Add(grezzo[i]);
                }
                else ris.Add(grezzo[i]);
            }
            var subitoArt = ris.Where(r => r.Tipo == TipoRiga.Prodotto && r.Portata == 0).ToList();
            if (subitoArt.Count > 0)
            {
                var subitoSep = ris.Where(r => r.Tipo == TipoRiga.Portata && r.Portata == 0).ToList();
                ris = subitoSep.Concat(subitoArt).Concat(ris.Where(r => r.Portata != 0)).ToList();
            }
            return ris;
        }

        // ══════════════ STORNO ══════════════

        private (List<JobStampa> stampe, List<StornoRegistrato> registro) PreparaStorni(bool stampaStorni, EsitoTavolo esito)
        {
            var stampe = new List<JobStampa>();
            var registro = new List<StornoRegistrato>();
            if (_storniPendenti.Count == 0) return (stampe, registro);

            foreach (var g in _storniPendenti.Where(r => r.Tipo == TipoRiga.Prodotto).GroupBy(r => r.CentroProduzioneId))
            {
                string nomeCentro = g.Key > 0 ? Stampe.NomeUscita(g.Key) : "N/A";
                var st = g.Key > 0 ? InstradamentoStampa.Risolvi(Stampe, Tavolo.SalaId, g.Key) : null;
                bool stampato = stampaStorni && st != null;
                if (stampato)
                {
                    var dati = new DatiComanda { NomeCentro = nomeCentro, CentroId = g.Key, NomeTavolo = NomeTavolo, NomeSala = NomeSala, Righe = g.ToList() };
                    stampe.Add(new JobStampa
                    {
                        Tipo = TipoJob.Storno, Descrizione = $"STORNO {Titolo} → {nomeCentro}",
                        Stampante = nomeCentro, IP = st.IP, Porta = st.Porta, ControlloStato = st.ControlloStato,
                        Dati = Finitura.Applica(LayoutComanda.Storno(dati, Cfg.StileComandaProdotto, Cfg.StileComandaVariante), st, Cfg.CicalinoComande)
                    });
                }
                else if (stampaStorni && g.Key > 0)
                    esito.Avvisi.Add($"{nomeCentro}: nessuna stampante impostata, storno NON stampato.");

                foreach (var r in g)
                    registro.Add(new StornoRegistrato
                    {
                        Sala = NomeSala, Tavolo = NomeTavolo, Centro = nomeCentro, Descrizione = r.Descrizione,
                        Quantita = r.Quantita, PrezzoCentesimi = r.PrezzoCentesimi, Stampato = stampato
                    });
            }
            LogManager.Info($"STORNO {Titolo}: {registro.Count} righe, {stampe.Count} stampe in coda");
            return (stampe, registro);
        }

        // ══════════════ ⏩ AVANZA = VAI PORTATA (btnAvanza_Click del PC) ══════════════

        public AnteprimaAvanza PreparaAvanza()
        {
            var righe = Carrello.Righe;
            Portate.Assegna(righe, Cfg.SeparatoriPortate);
            var logiche = righe.Where(Portate.IsSeparatoreReale).Select(r => r.Portata).Distinct().OrderBy(p => p).ToList();
            if (logiche.Count == 0)
                return new AnteprimaAvanza { Titolo = "NESSUNA PORTATA", Messaggio = "Non ci sono separatori nella comanda." };

            int corrente = Tavolo.PortataCorrente, massima = logiche.Max();
            int prossima = logiche.FirstOrDefault(p => p > corrente);
            if (prossima == 0 && corrente >= massima)
                return new AnteprimaAvanza { Titolo = "PORTATE COMPLETE", Messaggio = "Tutte le portate sono state inviate." };
            if (prossima == 0) prossima = corrente + 1;

            var sep = righe.FirstOrDefault(r => Portate.IsSeparatoreReale(r) && r.Portata == prossima);
            string nomeSep = sep != null ? Portate.Nome(sep) : $"SEGUE {prossima}";
            var conCentro = righe.Where(r => r.Tipo == TipoRiga.Prodotto && r.Portata == prossima && r.CentroProduzioneId > 0).ToList();
            var senzaCentro = righe.Where(r => r.Tipo == TipoRiga.Prodotto && r.Portata == prossima && r.CentroProduzioneId <= 0).ToList();

            if (conCentro.Count == 0)
            {
                if (senzaCentro.Count == 0)
                    return new AnteprimaAvanza { Titolo = "NESSUN ARTICOLO", Messaggio = $"Nessun articolo in portata '{nomeSep}'." };
                return new AnteprimaAvanza { Possibile = true, SenzaStampa = true, Portata = prossima, NomeSeparatore = nomeSep };
            }
            string lista = string.Join("\n", conCentro.Select(r => r.IsAPeso ? $"  {r.Quantita:0.000} {r.UnitaPeso} {r.Descrizione}" : $"  {(int)r.Quantita}x {r.Descrizione}"));
            return new AnteprimaAvanza
            {
                Possibile = true, Portata = prossima, NomeSeparatore = nomeSep,
                Titolo = $"⏩ VAI {nomeSep}", Messaggio = $"Manda in produzione:\n\n{lista}\n\nConfermi?"
            };
        }

        public EsitoTavolo EseguiAvanza(AnteprimaAvanza a) => ConRipristino(() => EseguiAvanzaInterno(a));

        private EsitoTavolo EseguiAvanzaInterno(AnteprimaAvanza a)
        {
            var esito = new EsitoTavolo();
            var righe = Carrello.Righe;
            var stampe = new List<JobStampa>();
            if (!a.SenzaStampa)
            {
                var gruppi = righe.Where(r => r.Tipo == TipoRiga.Prodotto && r.Portata == a.Portata && r.CentroProduzioneId > 0)
                                  .GroupBy(r => r.CentroProduzioneId).ToList();
                foreach (var g in gruppi)
                {
                    string nomeCentro = Stampe.NomeUscita(g.Key);
                    var st = InstradamentoStampa.Risolvi(Stampe, Tavolo.SalaId, g.Key);
                    if (st == null) { esito.Avvisi.Add($"{nomeCentro}: nessuna stampante impostata, VAI NON stampato."); continue; }
                    // articoli della portata non ancora trasmessi: partono con il VAI (come il PC), solo se la stampante c'è (regola 12)
                    foreach (var r in g.Where(r => !r.Comandata)) r.Comandata = true;
                    Dictionary<string, List<RigaScontrino>> altri = Cfg.StampaRiepilogoAltriCentri
                        ? gruppi.Where(x => x.Key != g.Key).ToDictionary(x => Stampe.NomeUscita(x.Key), x => x.ToList())
                        : null;
                    var dati = new DatiComanda
                    {
                        NomeCentro = nomeCentro, CentroId = g.Key, NomeTavolo = NomeTavolo, NomeSala = NomeSala,
                        NomeSeparatore = a.NomeSeparatore, Righe = g.ToList(), AltriCentri = altri
                    };
                    stampe.Add(new JobStampa
                    {
                        Tipo = TipoJob.Vai, Descrizione = $"VAI {a.NomeSeparatore} {Titolo} → {nomeCentro}",
                        Stampante = nomeCentro, IP = st.IP, Porta = st.Porta, ControlloStato = st.ControlloStato,
                        Dati = Finitura.Applica(LayoutComanda.Vai(dati, Cfg.StileComandaProdotto, 0, Cfg.StileComandaVariante), st, Cfg.CicalinoComande)
                    });
                }
            }
            Tavolo.PortataCorrente = a.Portata;
            _archivio.Salva(Tavolo, righe.ToList(), stampe, storniPendenti: _storniPendenti);
            Sveglia(stampe);
            esito.ComandeAccodate = stampe.Count;
            LogManager.Info($"VAI {a.NomeSeparatore} {Titolo}: {stampe.Count} stampe in coda");
            return esito;
        }

        // ══════════════ 🔀 SPOSTA / 🔗 UNISCI ══════════════

        /// <summary>
        /// 🔀 SPOSTA: salva il tavolo (storni compresi, le righe nuove restano da comandare) e lo sposta;
        /// se la destinazione è occupata i tavoli si scambiano. Dopo si torna alla sala.
        /// </summary>
        public string Sposta(int salaDest, int numeroDest, string nomeDestinazione)
        {
            if (!Carrello.HaArticoli) throw new InvalidOperationException("Il tavolo è vuoto: non c'è niente da spostare.");
            string da = Titolo;
            EsciSenzaInvio();
            bool scambio = _archivio.SpostaTavolo(Tavolo.Id, salaDest, numeroDest);
            return scambio ? $"{da} ↔ {nomeDestinazione}" : $"Spostato da {da} a {nomeDestinazione}.";
        }

        /// <summary>🔗 UNISCI: le righe dei tavoli scelti vengono aggiunte a questo tavolo (comande già fatte restano comandate).</summary>
        public void Unisci(IEnumerable<long> tavoliId)
        {
            SalvaBozza();
            _archivio.UnisciTavoli(Tavolo.Id, tavoliId);
            Carrello.Carica(_archivio.Righe(Tavolo.Id));
            // anche gli storni in sospeso dei tavoli uniti (altrimenti il prossimo salvataggio li cancellerebbe)
            _storniPendenti.Clear();
            _storniPendenti.AddRange(_archivio.StorniPendenti(Tavolo.Id));
            LogManager.Info($"UNISCI in {Titolo}: {Carrello.Righe.Count(r => r.Tipo == TipoRiga.Prodotto)} articoli");
        }

        // ══════════════ 📋 PRECONTO ══════════════

        /// <summary>Preconto sulla termica (non salvato come conto); il tavolo diventa giallo. Restituisce null se ok.</summary>
        public async Task<string> PrecontoAsync()
        {
            var imp = _impostazioni();
            if (!imp.AbilitaStampanteTermica) return "Stampante termica disabilitata.";
            var righe = Carrello.Fotografia();
            if (!righe.Any(r => r.Tipo == TipoRiga.Prodotto)) return "Non ci sono articoli.";
            if (righe.Where(r => r.ContaNelTotale).Sum(r => r.ImportoCentesimi) <= 0) return "Totale non valido.";
            var pre = imp.StampantePreconti(Tavolo.SalaId);
            if (!pre.Assegnata) return "Nessuna stampante PRECONTI impostata.\n\nImpostala in IMPOSTAZIONI → 🖨️ STAMPE.";
            var dati = Finitura.Applica(LayoutConto.Crea(righe, 0, imp.IntestazioneConto1, imp.IntestazioneConto2, imp.IntestazioneConto3, imp.AccorpaRigheConto, DateTime.Now, varianti: imp.VariantiSuConto),
                                        pre, imp.Sale.CicalinoComande);
            string errore = await AccessoStampanti.InviaAsync(pre.IP, pre.Porta, dati, TimeSpan.FromSeconds(10), pre.ControlloStato);
            if (errore != null) return $"Impossibile stampare su {pre.Descrizione}.\n{errore}\n\nPreconto NON stampato.";
            Tavolo.Preconto = true;
            _firmaPreconto = FirmaConto();
            SalvaBozza();
            return null;
        }

        // ══════════════ interni ══════════════

        /// <summary>Coperti del tavolo (per la banda "dove sono" della vendita).</summary>
        public int NumeroCoperti => Coperti();

        /// <summary>Coperti = quantità della riga "coperto" della sala.</summary>
        private int Coperti()
        {
            int id = Sala?.CopertoProdottoId ?? 0;
            if (id <= 0) return 0;
            return (int)Carrello.Righe.Where(r => r.Tipo == TipoRiga.Prodotto && r.ProdottoId == id).Sum(r => r.Quantita);
        }

        private void Sveglia(List<JobStampa> stampe) { if (stampe.Count > 0) _spooler.Sveglia(); }
    }
}
