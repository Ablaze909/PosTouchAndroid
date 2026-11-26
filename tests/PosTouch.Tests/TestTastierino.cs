using PosTouch.Core.Vendita;
using static PosTouch.Tests.Verifica;

namespace PosTouch.Tests
{
    public static class TestTastierino
    {
        private static void Batti(Tastierino t, string tasti) { foreach (var c in tasti) t.Premi(c.ToString()); }

        public static void Esegui()
        {
            var t = new Tastierino();
            Batti(t, "3"); t.Moltiplica(); Batti(t, "1,5");
            var (q, p) = t.PrendiPerArticolo();
            Ok(q == 3 && p == 150, "3 X 1,5 = 3 pezzi a 1,50");
            Batti(t, "2,5"); t.Euro(); (q, p) = t.PrendiPerArticolo();
            Ok(q == 1 && p == 250, "2,5 € = 1 pezzo a 2,50");
            Batti(t, "0"); (q, p) = t.PrendiPerArticolo();
            Ok(q == 1, "quantità 0 diventa 1");
            Batti(t, "999999999"); Ok(t.Input.Length <= 5, "massimo 5 cifre intere");
            t.Azzera();

            var v = new Tastierino { VirgolaImplicita = true };
            Batti(v, "350"); v.Euro(); (q, p) = v.PrendiPerArticolo();
            Ok(p == 350, "virgola implicita: 350 € = 3,50");
            Batti(v, "3"); v.Moltiplica(); Batti(v, "150"); (q, p) = v.PrendiPerArticolo();
            Ok(q == 3 && p == 150, "virgola implicita: 3 X 150 = 3 a 1,50");
            Batti(v, "2"); (q, p) = v.PrendiPerArticolo();
            Ok(q == 2 && p == null, "virgola implicita: le quantità restano intere");
            Batti(v, "2,5"); v.Euro(); (q, p) = v.PrendiPerArticolo();
            Ok(p == 250, "virgola implicita: se scrivo la virgola vale quella");
        }
    }
}
