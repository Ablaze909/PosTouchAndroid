using System.Collections.Generic;
using System.Linq;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Sale
{
    /// <summary>
    /// 🔗 UNISCI per portata: ogni articolo del tavolo unito va nella sua portata del tavolo di destinazione
    /// (SUBITO con SUBITO, SEGUE 2 con SEGUE 2), invece di finire in fondo all'ultima portata.
    /// Le righe di ogni tavolo devono già avere la propria Portata (Portate.Assegna sul tavolo da solo).
    /// </summary>
    public static class UnioneTavoli
    {
        public static List<RigaScontrino> Unisci(IEnumerable<IList<RigaScontrino>> tavoli, IList<string> separatori)
        {
            RigaScontrino subito = null;
            var separatoriPerNumero = new SortedDictionary<int, RigaScontrino>();
            var articoliPerPortata = new SortedDictionary<int, List<RigaScontrino>>();
            var coda = new List<RigaScontrino>();   // sconti, subtotali, separatori grafici: in fondo come sempre

            foreach (var righe in tavoli)
            {
                List<RigaScontrino> unita = null;   // articolo + gusti e varianti attaccati
                foreach (var r in righe)
                {
                    switch (r.Tipo)
                    {
                        case TipoRiga.Prodotto:
                            unita = Lista(articoliPerPortata, r.Portata);
                            unita.Add(r);
                            break;
                        case TipoRiga.Gusto:
                        case TipoRiga.Variante:
                            if (unita != null) unita.Add(r); else coda.Add(r);   // senza articolo (non dovrebbe succedere): non si perde
                            break;
                        case TipoRiga.Portata:
                            unita = null;
                            if (Portate.IsSubito(r)) subito ??= r;
                            else
                            {
                                int n = Portate.NumeroLogico(r, separatori);
                                if (!separatoriPerNumero.ContainsKey(n)) separatoriPerNumero[n] = r;   // un solo "SEGUE 2" anche se c'era in entrambi
                            }
                            break;
                        default:
                            unita = null;
                            coda.Add(r);
                            break;
                    }
                }
            }

            var ris = new List<RigaScontrino>();
            // portata 0 in testa (prima di ogni separatore valgono SUBITO); il separatore SUBITO resta se c'era
            if (articoliPerPortata.TryGetValue(0, out var zero) && zero.Count > 0)
            {
                if (subito != null && separatoriPerNumero.Count > 0) ris.Add(subito);
                ris.AddRange(zero);
            }
            var numeri = separatoriPerNumero.Keys.Union(articoliPerPortata.Keys.Where(k => k > 0)).OrderBy(k => k);
            foreach (int n in numeri)
            {
                if (separatoriPerNumero.TryGetValue(n, out var sep)) ris.Add(sep);
                if (articoliPerPortata.TryGetValue(n, out var art)) ris.AddRange(art);
            }
            ris.AddRange(coda);
            return ris;
        }

        private static List<RigaScontrino> Lista(SortedDictionary<int, List<RigaScontrino>> d, int portata)
        {
            if (!d.TryGetValue(portata, out var l)) d[portata] = l = new List<RigaScontrino>();
            return l;
        }
    }
}
