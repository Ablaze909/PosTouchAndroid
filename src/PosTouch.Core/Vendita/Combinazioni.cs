using System;
using System.Collections.Generic;
using System.Linq;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Vendita
{
    /// <summary>
    /// Prezzo di una combinazione (CalcolaPrezzoCombinazione / RicalcolaPrezzoCombinazione del PC):
    /// politica sui gusti (più caro / media / fisso) + varianti (intere o divise per i gusti se "proporzionali").
    /// Il prezzo di ogni gusto è salvato nella riga gusto (PrezzoOriginaleCentesimi), quello originale
    /// di ogni variante nella riga variante (PrezzoOriginaleCentesimi, con segno).
    /// </summary>
    public static class Combinazioni
    {
        /// <summary>Combinazione già chiusa (prezzo calcolato), non quella in corso.</summary>
        public static bool Chiusa(RigaScontrino padre) => !string.IsNullOrEmpty(padre.VariantiTesto) || padre.PrezzoCentesimi > 0;

        public static int PrezzoBase(Prodotto prodBase, IList<int> prezziGusti) => (PoliticaPrezzoCombinazione)prodBase.PoliticaPrezzo switch
        {
            PoliticaPrezzoCombinazione.PiuCaro => prezziGusti.Count > 0 ? prezziGusti.Max() : 0,
            PoliticaPrezzoCombinazione.Media => prezziGusti.Count > 0 ? (int)Math.Round(prezziGusti.Average(), MidpointRounding.AwayFromZero) : 0,
            _ => prodBase.PrezzoCentesimi
        };

        /// <summary>Ricalcola prezzo e testo comanda del padre dai suoi figli. Restituisce il prezzo (0 = base sconosciuta).</summary>
        public static int Ricalcola(Carrello carrello, RigaScontrino padre, Prodotto prodBase = null)
        {
            prodBase ??= padre.ProdottoId.HasValue ? Carrello.RisolviProdotto?.Invoke(padre.ProdottoId.Value) : null;
            if (prodBase == null) return padre.PrezzoCentesimi;
            int n = Math.Clamp(prodBase.NumeroGusti, 2, 8);
            var figli = carrello.FigliDi(padre);
            var prezziGusti = figli.Where(f => f.Tipo == TipoRiga.Gusto).Select(f => f.PrezzoOriginaleCentesimi).ToList();
            int varianti = 0;
            foreach (var v in figli.Where(f => f.Tipo == TipoRiga.Variante))
            {
                v.PrezzoCentesimi = prodBase.VariantiProporzionali
                    ? (int)Math.Round(v.PrezzoOriginaleCentesimi / (decimal)n, MidpointRounding.AwayFromZero)
                    : v.PrezzoOriginaleCentesimi;
                varianti += v.PrezzoCentesimi;
            }
            padre.PrezzoCentesimi = PrezzoBase(prodBase, prezziGusti) + varianti;
            padre.VariantiTesto = string.Join("|", figli.Select(f => f.Descrizione.Trim()));
            carrello.Notifica();
            return padre.PrezzoCentesimi;
        }
    }
}
