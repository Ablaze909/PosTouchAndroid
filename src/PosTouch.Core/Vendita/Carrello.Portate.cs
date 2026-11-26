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
                bool presente = Righe.Any(Portate.IsSubito);
                if (!presente && Righe.Any(Portate.IsSeparatoreReale))
                {
                    int pos = Righe.IndexOf(Righe.First(Portate.IsSeparatoreReale));
                    Righe.Insert(pos, new RigaScontrino { Tipo = TipoRiga.Portata, Descrizione = Portate.NomeSubito, Quantita = 0 });
                }
                Aggiornato();
                return 0;
            }
            var esistente = Righe.FirstOrDefault(r => Portate.IsSeparatoreReale(r) &&
                                                      Portate.Nome(r).Equals(nome.Trim(), StringComparison.OrdinalIgnoreCase));
            if (esistente == null)
            {
                esistente = new RigaScontrino { Tipo = TipoRiga.Portata, Descrizione = Portate.Descrizione(nome), Quantita = 0 };
                var blocco = InizioBloccoSconto();
                if (blocco == null) Righe.Add(esistente); else Righe.Insert(Righe.IndexOf(blocco), esistente);
            }
            Aggiornato();
            PortataTarget = esistente.Portata;
            return esistente.Portata;
        }

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
