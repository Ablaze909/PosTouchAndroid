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

            // ── a peso: la variante è un importo fisso, non moltiplicato per i Kg ──
            var mozzKg = new Prodotto { Id = 20, Descrizione = "MOZZARELLA", PrezzoCentesimi = 1000, RepartoFiscale = 1, CategoriaId = 1, VenditaAPeso = true, UnitaPeso = "Kg" };
            var regalo = new Variante { Id = 5, Nome = "CONFEZIONE REGALO", PrezzoAggiuntaCent = 100 };
            var cp = new Carrello();
            var rp = cp.Aggiungi(mozzKg, 0.5m, null, false);
            Ok(Varianti.TrovaTarget(cp, rp, false, out _) != null, "variante ammessa su articolo a peso");
            Ok(Varianti.Applica(cp, rp, regalo, true, false) == null, "variante applicata al peso");
            Uguale(600, cp.TotaleCent, "0,500 Kg × 10 € + 1 € fisso = 6,00");
            Uguale(1000, rp.PrezzoCentesimi, "il prezzo al Kg non cambia");
            cp.Rimuovi(new List<RigaScontrino> { cp.Righe[1] });
            Uguale(500, cp.TotaleCent, "tolta la variante: 5,00");

            // ── combinazione: il prezzo cambiato a mano resta, le varianti si sommano ──
            var coppa = new Prodotto { Id = 30, Descrizione = "COPPETTA", PrezzoCentesimi = 300, RepartoFiscale = 1, CategoriaId = 2, IsCombinazione = true, NumeroGusti = 2, Attivo = true };
            var pist = new Prodotto { Id = 31, Descrizione = "PISTACCHIO", PrezzoCentesimi = 300, RepartoFiscale = 1, CategoriaId = 2, Attivo = true };
            var noc = new Prodotto { Id = 32, Descrizione = "NOCCIOLA", PrezzoCentesimi = 300, RepartoFiscale = 1, CategoriaId = 2, Attivo = true };
            var vecchio = Carrello.RisolviProdotto;
            Carrello.RisolviProdotto = id => id == 30 ? coppa : id == 31 ? pist : id == 32 ? noc : null;
            try
            {
                var cc = new Carrello();
                var comb = CombinazioneInCorso.Inizia(cc, coppa, new[] { coppa, pist, noc }, out _);
                comb.AggiungiGusto(pist); comb.AggiungiGusto(noc); comb.Chiudi();
                var padre = comb.Padre;
                Uguale(300, padre.PrezzoCentesimi, "combinazione calcolata 3,00");
                Ok(cc.CambiaPrezzo(padre, 250), "prezzo combinazione cambiabile a mano");
                var gusto = cc.FigliDi(padre).First(f => f.Tipo == TipoRiga.Gusto);
                var panna = new Variante { Id = 6, Nome = "PANNA", PrezzoAggiuntaCent = 50 };
                Ok(Varianti.Applica(cc, gusto, panna, true, false) == null, "panna su un gusto");
                Uguale(300, padre.PrezzoCentesimi, "2,50 a mano + 0,50 panna = 3,00");
                cc.Rimuovi(new List<RigaScontrino> { cc.FigliDi(padre).First(f => f.Tipo == TipoRiga.Variante) });
                Uguale(250, padre.PrezzoCentesimi, "tolta la panna torna 2,50");
                Varianti.Applica(cc, gusto, panna, true, false);
                cc.CambiaPrezzo(padre, 30);   // a mano 0,30 con la panna dentro
                cc.Rimuovi(new List<RigaScontrino> { cc.FigliDi(padre).First(f => f.Tipo == TipoRiga.Variante) });
                Ok(padre.PrezzoCentesimi == 30 && cc.FigliDi(padre).Any(f => f.Tipo == TipoRiga.Variante), "togliere la panna non manda il prezzo sotto zero: resta tutto com'era");
            }
            finally { Carrello.RisolviProdotto = vecchio; }
        }
    }
}
