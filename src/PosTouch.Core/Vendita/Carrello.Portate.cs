using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PosTouch.Core.Modelli;
using PosTouch.Core.Sale;

namespace PosTouch.Core.Vendita
{
    public partial class Carrello
    {
        // ══════════════ PORTATE ══════════════

        /// <summary>
        /// FUNZIONI → separatore (btnFunzioni_Click del PC): se esiste già diventa la destinazione dei prossimi
        /// articoli, altrimenti viene aggiunto in fondo. "SUBITO" va in testa. Restituisce la portata logica.
        /// </summary>
        public int ScegliPortata(string nome)
        {
            if (nome == null) return PortataTarget ?? 0;
            if (nome.Contains("SUBITO"))
            {
                PortataTarget = 0;
                var subito = Righe.FirstOrDefault(Portate.IsSubito);
                TogliSeparatoreVuoto(subito);
                if (subito == null && Righe.Any(Portate.IsSeparatoreReale))
                {
                    subito = NuovoSubitoInTesta();
                    _separatoreVuoto = subito;
                }
                Aggiornato();
                return 0;
            }
            var esistente = Righe.FirstOrDefault(r => Portate.IsSeparatoreReale(r) &&
                                                      Portate.Nome(r).Equals(nome.Trim(), StringComparison.OrdinalIgnoreCase));
            TogliSeparatoreVuoto(esistente);
            if (esistente == null)
            {
                esistente = new RigaScontrino { Tipo = TipoRiga.Portata, Descrizione = Portate.Descrizione(nome), Quantita = 0 };
                InserisciIntestazione(esistente, Portate.NumeroLogico(esistente, SeparatoriPortate));
                _separatoreVuoto = esistente;
            }
            Aggiornato();
            PortataTarget = esistente.Portata;
            return esistente.Portata;
        }

        /// <summary>Separatore aggiunto dall'ultima scelta di portata: se resta senza articoli si toglie alla scelta dopo.</summary>
        private RigaScontrino _separatoreVuoto;

        /// <summary>
        /// Si passa da una portata all'altra senza battere nulla (caselle INIZIO / SEGUE…): il separatore
        /// aggiunto prima e rimasto vuoto sparisce, così la comanda non si riempie di intestazioni vuote.
        /// I separatori con articoli, o già presenti prima della scelta, non si toccano mai.
        /// </summary>
        private void TogliSeparatoreVuoto(RigaScontrino tranne)
        {
            var s = _separatoreVuoto;
            if (s == null || ReferenceEquals(s, tranne)) return;
            _separatoreVuoto = null;
            int i = Righe.IndexOf(s);
            if (i < 0) return;
            for (int j = i + 1; j < Righe.Count; j++)
            {
                var r = Righe[j];
                if (r.Tipo is TipoRiga.Portata or TipoRiga.Separatore or TipoRiga.Subtotale or TipoRiga.Sconto) break;
                if (r.Tipo is TipoRiga.Prodotto or TipoRiga.Gusto or TipoRiga.Variante) return;   // ha articoli: resta
            }
            Righe.RemoveAt(i);
        }

        /// <summary>
        /// Intestazione SUBITO/INIZIO in testa alla lista: gli articoli battuti prima di ogni separatore sono già
        /// della prima uscita e devono stare SOTTO l'intestazione (prima finivano sopra e la sezione sembrava divisa).
        /// </summary>
        private RigaScontrino NuovoSubitoInTesta()
        {
            var subito = new RigaScontrino { Tipo = TipoRiga.Portata, Descrizione = Portate.NomeSubito, Quantita = 0 };
            Righe.Insert(0, subito);
            return subito;
        }

        /// <summary>Intestazione della portata scelta, ricreata se nel frattempo è stata cancellata (righe eliminate).</summary>
        private RigaScontrino SeparatoreDi(int portata)
        {
            var sep = Righe.FirstOrDefault(x => Portate.IsSeparatoreReale(x) && x.Portata == portata);
            if (sep != null) return sep;
            string nome = SeparatoriPortate != null && portata - 1 < SeparatoriPortate.Count ? SeparatoriPortate[portata - 1]
                        : portata == 1 ? "SEGUE" : $"SEGUE {portata}";
            sep = new RigaScontrino { Tipo = TipoRiga.Portata, Descrizione = Portate.Descrizione(nome), Quantita = 0, Portata = portata };
            InserisciIntestazione(sep, portata);
            return sep;
        }

        /// <summary>
        /// Intestazione nuova (o ricreata) messa al suo posto: prima della prima portata con numero più alto,
        /// altrimenti in fondo (prima del blocco sconto). Prima finiva sempre in fondo: cancellato SEGUE e rimesso,
        /// in cucina usciva "SEGUE 2" prima di "SEGUE".
        /// </summary>
        private void InserisciIntestazione(RigaScontrino intestazione, int numero)
        {
            var blocco = InizioBloccoSconto();
            int fine = blocco == null ? Righe.Count : Righe.IndexOf(blocco);
            int pos = fine;
            for (int i = 0; i < fine; i++)
                if (Portate.IsSeparatoreReale(Righe[i]) && Portate.NumeroLogico(Righe[i], SeparatoriPortate) > numero) { pos = i; break; }
            if (pos >= Righe.Count) Righe.Add(intestazione); else Righe.Insert(pos, intestazione);
        }

        /// <summary>Portata dove andrà il prossimo articolo (0 = INIZIO/SUBITO): la casella accesa sotto lo scontrino.</summary>
        public int PortataAttiva => PortataDestinazione();

        /// <summary>Riordino delle sezioni portata (alla chiusura del tavolo, come il PC).</summary>
        public void RiordinaPortate()
        {
            var blocco = Righe.Where(r => r.Tipo is TipoRiga.Separatore or TipoRiga.Subtotale or TipoRiga.Sconto).ToList();
            // gusti e varianti restano attaccati al loro articolo (fanno parte della stessa sezione)
            var area = Righe.Where(r => r.Tipo is TipoRiga.Prodotto or TipoRiga.Portata or TipoRiga.Gusto or TipoRiga.Variante).ToList();
            var ordinate = Portate.Riordina(area, SeparatoriPortate);
            if (ordinate.SequenceEqual(area)) return;
            Righe.Clear();
            foreach (var r in ordinate.Concat(blocco)) Righe.Add(r);
            Aggiornato();
        }

        /// <summary>Portata che riceverà il prossimo articolo.</summary>
        private int PortataDestinazione()
        {
            if (PortataTarget.HasValue) return PortataTarget.Value;
            var ultimo = Righe.LastOrDefault(r => r.Tipo == TipoRiga.Portata);
            return ultimo == null || Portate.IsSubito(ultimo) ? 0 : ultimo.Portata;
        }

        /// <summary>Copia delle righe da usare per l'emissione (il carrello può cambiare nel frattempo).</summary>
        public List<RigaScontrino> Fotografia() => Righe.Select(r => r.Copia()).ToList();
    }
}
