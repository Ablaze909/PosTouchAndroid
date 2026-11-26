using System;
using System.Collections.Generic;
using System.Linq;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Dati;
using PosTouch.Core.Diagnostica;
using PosTouch.Core.Modelli;
using PosTouch.Core.Stampa;

namespace PosTouch.Core.Sale
{
    /// <summary>
    /// COMANDA DA BANCO (come il PC): chiudendo scontrino o conto al banco, gli articoli con un'uscita
    /// vanno in comanda con un numero eliminacode, stampato anche in appendice allo scontrino o sul conto
    /// (o su un ticket della termica preconti, se scelto).
    /// Uso: <see cref="Prepara"/> prima di emettere → <see cref="Invia"/> a documento uscito.
    /// </summary>
    public class ComandaBanco
    {
        private readonly ArchivioTavoli _archivio;
        private readonly SpoolerComande _spooler;
        private readonly Func<Impostazioni> _impostazioni;
        private readonly Func<Catalogo> _catalogo;

        public ComandaBanco(ArchivioTavoli archivio, SpoolerComande spooler, Func<Impostazioni> impostazioni, Func<Catalogo> catalogo)
        {
            _archivio = archivio; _spooler = spooler; _impostazioni = impostazioni; _catalogo = catalogo;
        }

        private ConfigStampe Stampe => _impostazioni().Stampe ?? new ConfigStampe();
        public bool Attiva => _impostazioni().Sale.AbilitaComande && Stampe.ComandeDaBanco;

        /// <summary>Righe che vanno in comanda: articoli con un'uscita, categorie non escluse.</summary>
        public List<RigaScontrino> RigheDaComandare(IEnumerable<RigaScontrino> righe)
        {
            if (!Attiva || righe == null) return new List<RigaScontrino>();
            var escluse = new HashSet<int>(((IEnumerable<Categoria>)_catalogo()?.TutteCategorie ?? Enumerable.Empty<Categoria>()).Where(c => c.EscludiComandaBanco).Select(c => c.Id));
            return righe.Where(r => r.Tipo == TipoRiga.Prodotto && r.CentroProduzioneId > 0 && !escluse.Contains(r.CategoriaId)).ToList();
        }

        /// <summary>Numero eliminacode per questo documento (0 = niente comanda). Da chiamare PRIMA di emettere.</summary>
        public int Prepara(IEnumerable<RigaScontrino> righe)
        {
            if (RigheDaComandare(righe).Count == 0) return 0;
            int n = _archivio.ProssimoEliminacode();
            LogManager.Info($"COMANDA BANCO: eliminacode {n}");
            return n;
        }

        /// <summary>Il numero va in appendice allo scontrino (false = su ticket della termica).</summary>
        public bool InAppendice => !Stampe.TicketEliminacodeSuTermica;

        /// <summary>
        /// Documento uscito: accoda le comande per uscita (e il ticket eliminacode se scelto).
        /// Restituisce gli avvisi da mostrare (uscite senza stampante), mai eccezioni.
        /// </summary>
        public List<string> Invia(IEnumerable<RigaScontrino> righe, int eliminacode)
        {
            var avvisi = new List<string>();
            if (eliminacode <= 0) return avvisi;
            try
            {
                var imp = _impostazioni();
                var st = Stampe;
                var cfg = imp.Sale;
                var daComandare = RigheDaComandare(righe);
                if (daComandare.Count == 0) return avvisi;

                int progressivo = _archivio.ProssimoProgressivoComanda();
                string titolo = $"BANCO  N° {eliminacode:D3}";
                var stampe = new List<JobStampa>();
                var gruppi = daComandare.GroupBy(r => r.CentroProduzioneId).ToList();
                foreach (var g in gruppi)
                {
                    string nome = st.NomeUscita(g.Key);
                    var par = st.StampanteUscita(ConfigStampe.SalaBanco, g.Key);
                    if (par == null)
                    {
                        avvisi.Add($"{nome}: nessuna stampante per il BANCO, comanda N° {eliminacode:D3} NON stampata.");
                        continue;
                    }
                    Dictionary<string, List<RigaScontrino>> altri = cfg.StampaRiepilogoAltriCentri ? st.AltriPer(g.Key, gruppi) : null;
                    var dati = new DatiComanda
                    {
                        NomeCentro = nome, CentroId = g.Key, NomeTavolo = titolo, NomeSala = "",
                        Progressivo = progressivo, Righe = g.ToList(), AltriCentri = altri
                    };
                    var contenuto = LayoutComanda.Comanda(dati, cfg.StileComandaProdotto, cfg.StileComandaSeparatore, 0, cfg.AccorpaStampaComanda, cfg.StileComandaVariante);
                    stampe.Add(new JobStampa
                    {
                        Tipo = TipoJob.Comanda, Descrizione = $"COMANDA {titolo} → {nome}",
                        Stampante = nome, IP = par.IP, Porta = par.Porta, ControlloStato = par.ControlloStato,
                        Dati = Finitura.Applica(contenuto, par, cfg.CicalinoComande)
                    });
                }

                if (st.TicketEliminacodeSuTermica)
                {
                    var pre = st.Di(ConfigStampe.SalaBanco).Preconto;
                    if (pre.Assegnata)
                        stampe.Add(new JobStampa
                        {
                            Tipo = TipoJob.Comanda, Descrizione = $"TICKET N° {eliminacode:D3}", Stampante = "PRECONTI", IP = pre.IP, Porta = pre.Porta, ControlloStato = pre.ControlloStato,
                            Dati = Finitura.Applica(LayoutComanda.TicketEliminacode(eliminacode, imp.IntestazioneConto1, DateTime.Now), pre, cfg.CicalinoComande)
                        });
                    else avvisi.Add($"Ticket eliminacode N° {eliminacode:D3} non stampato: nessuna stampante PRECONTI per il banco.");
                }

                if (stampe.Count > 0)
                {
                    _archivio.AccodaStampe(stampe);
                    _spooler.Sveglia();
                }
                LogManager.Info($"COMANDA BANCO N° {eliminacode}: {daComandare.Count} righe, {stampe.Count} stampe in coda");
            }
            catch (Exception ex)
            {
                LogManager.Error($"COMANDA BANCO N° {eliminacode}: {ex}");
                avvisi.Add($"Comanda N° {eliminacode:D3} NON inviata: {ex.Message}");
            }
            return avvisi;
        }
    }
}
