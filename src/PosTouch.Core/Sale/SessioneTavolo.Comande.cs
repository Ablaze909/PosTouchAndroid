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
    public partial class SessioneTavolo
    {
        // ══════════════ COMANDA (InviaComandaAutomaticaAsync del PC) ══════════════

        /// <param name="righe">Righe da comandare: null = carrello; le righe vendute quando il tavolo si chiude pagato.</param>
        private List<JobStampa> PreparaComande(bool stampa, EsitoTavolo esito, IList<RigaScontrino> righe = null)
        {
            bool chiusura = righe != null;   // tavolo che si chiude: ciò che non parte ora non partirà più
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
            int coperti = Coperti(righe);   // pagato subito: il carrello è già vuoto, i coperti sono nelle righe vendute
            List<RigaScontrino> flusso = Cfg.AbilitaPortate ? FlussoPortate(righe, Tavolo.PortataCorrente, sempreSubito: true) : null;
            var gruppi = nuove.GroupBy(r => r.CentroProduzioneId).ToList();
            var nonInviate = new List<RigaScontrino>();

            foreach (var g in gruppi)
            {
                var st = InstradamentoStampa.Risolvi(Stampe, Tavolo.SalaId, g.Key);
                string nomeCentro = Stampe.NomeUscita(g.Key);
                if (st == null)
                {
                    // righe lasciate "da comandare": partiranno quando la stampante sarà impostata
                    esito.Avvisi.Add(chiusura
                        ? $"{nomeCentro}: nessuna stampante impostata, comanda NON stampata ({g.Sum(r => r.Quantita):0.###} pz).\nIl tavolo è chiuso: AVVISA A VOCE il reparto:\n" +
                          string.Join("\n", g.Select(r => $"• {r.Quantita:0.###} x {r.Descrizione}"))
                        : $"{nomeCentro}: nessuna stampante impostata, comanda NON stampata ({g.Sum(r => r.Quantita):0.###} pz).\nGli articoli restano da comandare.");
                    LogManager.Warning($"COMANDA {Titolo}: centro {nomeCentro} senza stampante" + (chiusura ? " (tavolo pagato: articoli NON inviati)" : ""));
                    nonInviate.AddRange(g);
                    continue;
                }
                // RIORDINO per centro: solo se QUESTO centro ha già ricevuto articoli della stessa portata
                bool riordino = !primaComanda && g.Any(r => righe.Any(prev => prev != r && prev.Tipo == TipoRiga.Prodotto && prev.Comandata
                                                                       && prev.CentroProduzioneId == g.Key && prev.Portata == r.Portata));
                Dictionary<string, List<RigaScontrino>> altri = null;
                if (Cfg.StampaRiepilogoAltriCentri && !Cfg.AbilitaPortate)
                    altri = Stampe.AltriPer(g.Key, gruppi);

                var dati = new DatiComanda
                {
                    NomeCentro = nomeCentro, CentroId = g.Key, NomeTavolo = NomeTavolo, NomeSala = NomeSala,
                    Progressivo = progressivo, Coperti = coperti, Riordino = riordino,
                    Righe = g.ToList(), AltriCentri = altri, FlussoPortate = FlussoCentro(g.Key, flusso, righe)
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
        /// Prima uscita: nel PRIMO ordine esce senza intestazione (né INIZIO né SUBITO); "!! SUBITO !!" solo se
        /// è un riordino nella prima uscita (lì c'erano già articoli mandati) o se ci sono articoli di categorie ⚡ SUBITO.
        /// </summary>
        /// <param name="portataCorrente">Ultima portata già mandata col VAI: un articolo aggiunto lì esce con "VAI …" (la cucina non lo tiene in attesa).</param>
        /// <param name="sempreSubito">Comande vere: l'intestazione SUBITO si mette sempre e la decide poi ogni reparto (FlussoCentro).</param>
        public static List<RigaScontrino> FlussoPortate(IList<RigaScontrino> righe, int portataCorrente = 0, bool sempreSubito = false)
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
            ris.RemoveAll(r => r.Tipo == TipoRiga.Portata && r.Portata == 0);   // l'intestazione della prima uscita si decide qui sotto
            if (subitoArt.Count > 0)
            {
                // solo ai tavoli che usano i separatori (SEGUE…): senza, le comande restano come sempre, senza intestazioni
                bool conSeparatori = righe.Any(Portate.IsSeparatoreReale);
                bool riordino = conSeparatori && righe.Any(r => r.Tipo == TipoRiga.Prodotto && r.Comandata && r.Portata == 0);
                bool urgenti = conSeparatori && Portate.CategoriaSubito != null && subitoArt.Any(r => Portate.CategoriaSubito(r.CategoriaId));
                var testa = new List<RigaScontrino>();
                if (riordino || urgenti || (sempreSubito && conSeparatori))
                    testa.Add(righe.FirstOrDefault(Portate.IsSubito)
                              ?? new RigaScontrino { Tipo = TipoRiga.Portata, Descrizione = Portate.NomeSubito, Quantita = 0, Portata = 0 });
                ris = testa.Concat(subitoArt).Concat(ris.Where(r => r.Portata != 0)).ToList();
            }
            // portate già avanzate (VAI fatto): "VAI SEGUE" invece di "SEGUE", altrimenti la cucina aspetta un VAI che non arriva più
            if (portataCorrente > 0)
                for (int i = 0; i < ris.Count; i++)
                    if (Portate.IsSeparatoreReale(ris[i]) && ris[i].Portata > 0 && ris[i].Portata <= portataCorrente)
                    {
                        var vai = ris[i].Copia();
                        vai.Descrizione = Portate.Descrizione("VAI " + Portate.Nome(ris[i]));
                        vai.Portata = ris[i].Portata;
                        ris[i] = vai;
                    }
            return ris;
        }

        /// <summary>
        /// Flusso per un reparto: "!! SUBITO !!" solo se QUESTO reparto ha già ricevuto qualcosa da questo tavolo
        /// (riordino: magari ha un SEGUE in attesa e deve capire che questi si fanno ora) o ha articoli di categorie
        /// ⚡ SUBITO. Una bibita del bar non lo fa più uscire anche in cucina.
        /// </summary>
        private List<RigaScontrino> FlussoCentro(int centro, List<RigaScontrino> flusso, IList<RigaScontrino> righe)
        {
            var f = Stampe.FlussoPer(centro, flusso);
            var subito = f?.FirstOrDefault(Portate.IsSubito);
            if (subito == null) return f;
            bool riordino = righe.Any(r => r.Tipo == TipoRiga.Prodotto && r.Comandata && r.CentroProduzioneId == centro);
            bool urgenti = Portate.CategoriaSubito != null
                           && f.Any(r => r.Tipo == TipoRiga.Prodotto && r.Portata == 0 && r.CentroProduzioneId == centro && Portate.CategoriaSubito(r.CategoriaId));
            if (!riordino && !urgenti) f.Remove(subito);
            return f;
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
    }
}
