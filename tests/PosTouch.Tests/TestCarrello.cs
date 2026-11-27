using System.Collections.Generic;
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

            // ── caselle portata sotto lo scontrino: si sceglie dove vanno i prossimi articoli ──
            var cp = new Carrello { SeparatoriPortate = new List<string> { "SEGUE", "SEGUE 2", "SEGUE 3" } };
            cp.ScegliPortata("SEGUE");
            cp.ScegliPortata("SEGUE 2");
            Ok(!cp.Righe.Any(x => Portate.IsSeparatoreReale(x) && Portate.Nome(x) == "SEGUE"), "casella cambiata senza battere: il SEGUE vuoto sparisce");
            Uguale(2, cp.PortataAttiva, "casella accesa: SEGUE 2");
            var a2 = cp.Aggiungi(primo, 1, null, false);
            Uguale(2, a2.Portata, "l'articolo va nel SEGUE 2");
            cp.ScegliPortata("SEGUE 3");
            cp.ScegliPortata("SEGUE 2");
            Ok(cp.Righe.Any(x => Portate.Nome(x) == "SEGUE 2") && !cp.Righe.Any(x => Portate.Nome(x) == "SEGUE 3"), "SEGUE 2 con articoli resta, SEGUE 3 vuoto sparisce");
            cp.ScegliPortata("SUBITO");
            Uguale(0, cp.PortataAttiva, "casella INIZIO accesa");
            var a0 = cp.Aggiungi(bibita, 1, null, false);
            Ok(a0.Portata == 0 && cp.Righe.IndexOf(a0) < cp.Righe.IndexOf(cp.Righe.First(Portate.IsSeparatoreReale)), "INIZIO: l'articolo va prima dei SEGUE");
            Uguale(2, cp.Righe.First(x => x.ProdottoId == 10).Portata, "gli articoli già battuti restano nella loro portata");

            // ── caso segnalato: articoli in INIZIO dopo aver già usato i SEGUE ──
            var ci = new Carrello { SeparatoriPortate = new List<string> { "SEGUE", "SEGUE 2" } };
            var pane = new Prodotto { Id = 30, Descrizione = "PANE", PrezzoCentesimi = 100, RepartoFiscale = 1, CategoriaId = 4 };
            var acqua = new Prodotto { Id = 31, Descrizione = "ACQUA", PrezzoCentesimi = 200, RepartoFiscale = 1, CategoriaId = 4 };
            var x0 = ci.Aggiungi(pane, 1, null, true);          // battuto senza scegliere nulla: prima uscita
            ci.ScegliPortata("SEGUE");
            ci.Aggiungi(primo, 1, null, true);
            ci.ScegliPortata("SEGUE 2");
            ci.Aggiungi(primo, 1, null, true);
            ci.ScegliPortata("SUBITO");
            var x1 = ci.Aggiungi(acqua, 1, null, true);
            Ok(ci.Righe[0].Tipo == TipoRiga.Portata && Portate.IsSubito(ci.Righe[0]), "INIZIO: intestazione in testa, nessun articolo sopra");
            Ok(x0.Portata == 0 && x1.Portata == 0 && ci.Righe.IndexOf(x0) < ci.Righe.IndexOf(ci.Righe.First(Portate.IsSeparatoreReale))
               && ci.Righe.IndexOf(x1) < ci.Righe.IndexOf(ci.Righe.First(Portate.IsSeparatoreReale)), "pane e acqua insieme sotto INIZIO");
            var x2 = ci.Aggiungi(acqua, 1, null, true);
            Ok(x2 == x1 && x1.Quantita == 2, "seconda acqua in INIZIO: accorpata");
            ci.ScegliPortata("SEGUE");
            var x3 = ci.Aggiungi(acqua, 1, null, true);
            Ok(x3 != x1 && x3.Portata == 1, "acqua nel SEGUE: riga separata, non accorpata con quella di INIZIO");
            // intestazione cancellata a mano: la casella la rimette
            ci.Righe.Remove(ci.Righe.First(x => Portate.Nome(x) == "SEGUE 2"));
            ci.ScegliPortata("SEGUE 2");
            var x4 = ci.Aggiungi(pane, 1, null, true);
            Ok(x4.Portata == 2 && ci.Righe.Count(x => Portate.Nome(x) == "SEGUE 2") == 1, "SEGUE 2 rimesso, articolo sotto");

            // ── prova a caso: ogni articolo resta nella portata scelta, intestazioni uniche, niente articoli sopra la prima ──
            var caso = new System.Random(7);
            var nomiP = new[] { "SUBITO", "SEGUE", "SEGUE 2", "SEGUE 3" };
            var cr = new Carrello { SeparatoriPortate = new List<string> { "SEGUE", "SEGUE 2", "SEGUE 3" } };
            var attese = new Dictionary<RigaScontrino, int>();
            var arts = new[] { pane, acqua, primo };
            bool tuttoOk = true;
            for (int k = 0; k < 400 && tuttoOk; k++)
            {
                int azione = caso.Next(10);
                if (azione < 3) cr.ScegliPortata(nomiP[caso.Next(4)]);
                else if (azione == 3 && cr.Righe.Any(x => x.Tipo == TipoRiga.Prodotto))
                {
                    var via = cr.Righe.Where(x => x.Tipo == TipoRiga.Prodotto).ElementAt(caso.Next(cr.Righe.Count(x => x.Tipo == TipoRiga.Prodotto)));
                    cr.Righe.Remove(via); attese.Remove(via);
                }
                else
                {
                    int dest = cr.PortataAttiva;
                    var nuova = cr.Aggiungi(arts[caso.Next(3)], 1, null, caso.Next(2) == 0);
                    attese[nuova] = dest;
                }
                foreach (var kv in attese) if (kv.Key.Portata != kv.Value) tuttoOk = false;
                if (cr.Righe.Any(Portate.IsSeparatoreReale) && cr.Righe.Count > 0 && cr.Righe[0].Tipo != TipoRiga.Portata && cr.Righe.Any(Portate.IsSubito)) tuttoOk = false;
                if (cr.Righe.Where(x => x.Tipo == TipoRiga.Portata).GroupBy(x => x.Descrizione).Any(g => g.Count() > 1)) tuttoOk = false;
            }
            Ok(tuttoOk, "400 operazioni a caso: portate sempre giuste");

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
