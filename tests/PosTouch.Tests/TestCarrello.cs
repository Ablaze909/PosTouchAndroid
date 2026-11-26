using System.Linq;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;
using PosTouch.Core.Sale;
using static PosTouch.Tests.Verifica;

namespace PosTouch.Tests
{
    public static class TestCarrello
    {
        public static readonly Prodotto Caffe = new Prodotto { Id = 1, Descrizione = "CAFFE", PrezzoCentesimi = 120, RepartoFiscale = 1, CategoriaId = 7 };
        public static readonly Prodotto Acqua = new Prodotto { Id = 2, Descrizione = "ACQUA", PrezzoCentesimi = 100, RepartoFiscale = 2, CategoriaId = 5 };

        public static void Esegui()
        {
            var c = new Carrello();
            c.Aggiungi(Caffe, 1, null, true); c.Aggiungi(Caffe, 1, null, true);
            Ok(c.Righe.Count == 1 && c.Righe[0].Quantita == 2, "due caffè accorpati in una riga");
            c.Aggiungi(Acqua, 1, null, false);
            Ok(c.ScontoPercentuale(10), "sconto 10% accettato");
            Uguale(306, c.TotaleCent, "sconto 10% su 3,40");
            c.Aggiungi(Acqua, 1, null, false);
            Uguale(396, c.TotaleCent, "articolo battuto dopo lo sconto: ricalcolo");
            c.Rimuovi(c.Righe.Where(r => r.Tipo == TipoRiga.Prodotto).ToList());
            Ok(c.Vuoto, "tolti tutti gli articoli: carrello vuoto (sconto compreso)");

            c.Aggiungi(Caffe, 1, null, false);
            Ok(c.ScontoValore(100), "sconto a valore");
            Uguale(20, c.TotaleCent, "sconto 1,00 su 1,20");
            c.Svuota();

            Ok(c.Aggiungi(Caffe, 1000, null, false) == null, "quantità oltre il massimo rifiutata");
            Ok(c.Aggiungi(Caffe, 1, 0, false) == null || c.TotaleCent == 0, "prezzo zero non entra nel totale");
            c.Svuota();

            var r = c.Aggiungi(Caffe, 3, null, false);
            var uno = c.SeparaUnPezzo(r);
            Ok(uno != null && r.Quantita == 2 && uno.Quantita == 1, "stacca un pezzo: 3 → 2 + 1");
            Uguale(360, c.TotaleCent, "totale invariato dopo lo stacco");
            Ok(c.SeparaUnPezzo(uno) == null, "riga da 1 pezzo non si stacca");

            // ── ⚡ SUBITO automatico della categoria ──
            c = new Carrello();
            var bibita = new Prodotto { Id = 9, Descrizione = "COCA", PrezzoCentesimi = 300, RepartoFiscale = 1, CategoriaId = 3, Subito = true };
            var primo = new Prodotto { Id = 10, Descrizione = "SPAGHETTI", PrezzoCentesimi = 900, RepartoFiscale = 1, CategoriaId = 4 };
            var b0 = c.Aggiungi(bibita, 1, null, true);
            Uguale(0, b0.Portata, "senza separatori: la bibita è nella prima uscita");
            c.Svuota();
            c.ScegliPortata("SEGUE");
            c.Aggiungi(primo, 1, null, true);
            var b1 = c.Aggiungi(bibita, 1, null, true);
            Ok(b1.Portata == 0 && c.Righe.Any(Portate.IsSubito), "con SEGUE scelto la bibita va comunque in SUBITO");
            var spa = c.Righe.First(x => x.ProdottoId == 10);
            Ok(spa.Portata > 0, "gli spaghetti restano nel SEGUE");
            var dopo = c.Aggiungi(primo, 1, null, false);
            Ok(dopo.Portata == spa.Portata, "dopo la bibita si continua a battere nel SEGUE scelto");
            Ok(c.Righe.IndexOf(c.Righe.First(Portate.IsSubito)) < c.Righe.IndexOf(c.Righe.First(Portate.IsSeparatoreReale)), "SUBITO prima del SEGUE");
            c.Aggiungi(bibita, 1, null, true);
            Uguale(1, c.Righe.Count(Portate.IsSubito), "un solo separatore SUBITO");

            // ── listini 1-4 (come il PC) ──
            var vino = new Prodotto { Id = 20, Descrizione = "VINO", PrezzoCentesimi = 1000, Prezzo2Centesimi = 1200, Prezzo4Centesimi = 0, RepartoFiscale = 1, CategoriaId = 3 };
            Uguale(1200, vino.PrezzoListino(2), "listino 2");
            Uguale(1000, vino.PrezzoListino(4), "listino vuoto: prezzo del listino 1");
            Uguale(1000, vino.PrezzoListino(9), "listino fuori range: listino 1");
            var cl = new Carrello { Listino = 2 };
            var rv = cl.Aggiungi(vino, 2, null, true);
            Ok(rv.PrezzoCentesimi == 1200 && rv.PrezzoOriginaleCentesimi == 1200 && cl.TotaleCent == 2400, "al tavolo con listino 2 il vino costa 12,00");
            cl.Listino = 1;
            var rv1 = cl.Aggiungi(vino, 1, null, true);
            Ok(rv1 != rv && rv1.PrezzoCentesimi == 1000 && rv.PrezzoCentesimi == 1200, "cambiando listino le righe già battute restano");
            cl.Listino = 7;
            Uguale(4, cl.Listino, "listino limitato a 1-4");
            Uguale(2, b1.Quantita, "le bibite SUBITO si accorpano tra loro");
        }
    }
}
