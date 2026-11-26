using System.Linq;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;
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
        }
    }
}
