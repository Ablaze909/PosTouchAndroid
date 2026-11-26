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
    }
}
