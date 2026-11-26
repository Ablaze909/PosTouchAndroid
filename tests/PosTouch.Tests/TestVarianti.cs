using System.Collections.Generic;
using System.Linq;
using PosTouch.Core.Modelli;
using PosTouch.Core.Vendita;
using static PosTouch.Tests.Verifica;

namespace PosTouch.Tests
{
    public static class TestVarianti
    {
        public static void Esegui()
        {
            var pizza = new Prodotto { Id = 10, Descrizione = "MARGHERITA", PrezzoCentesimi = 600, RepartoFiscale = 1, CategoriaId = 1 };
            var mozz = new Variante { Id = 1, Nome = "MOZZARELLA", PrezzoAggiuntaCent = 100 };
            var cip = new Variante { Id = 2, Nome = "CIPOLLA" };
            var cotta = new Variante { Id = 3, Nome = "BEN COTTA", Tipo = TipoVariante.Preparazione };
            var c = new Carrello();
            var r = c.Aggiungi(pizza, 1, null, true);
            Ok(Varianti.ApplicaTutte(c, r, new List<Variante> { mozz, cotta }, true, false) == null, "due varianti insieme");
            Uguale(700, c.TotaleCent, "+ mozzarella 1,00");
            Ok(c.Righe[1].Descrizione == "+ MOZZARELLA" && c.Righe[2].Descrizione == "BEN COTTA", "segno solo sulle varianti standard");
            Varianti.Applica(c, r, cip, false, false);
            Ok(c.Righe.Last().Descrizione == "- CIPOLLA", "VAR- con segno meno");
            var t = Varianti.TrovaTarget(c, c.Righe[1], false, out _);
            Ok(ReferenceEquals(t, r), "toccando una variante il target è il suo articolo");
            c.Rimuovi(new List<RigaScontrino> { c.Righe[1] });
            Uguale(600, r.PrezzoCentesimi, "togliendo la mozzarella il prezzo torna 6,00");
            var b = c.Aggiungi(pizza, 1, null, true);
            Ok(!ReferenceEquals(b, r), "una pizza nuova non si accorpa a quella con varianti");
        }
    }
}
