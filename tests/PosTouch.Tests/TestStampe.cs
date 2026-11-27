using System.Linq;
using System.Collections.Generic;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Modelli;
using PosTouch.Core.Stampa;
using static PosTouch.Tests.Verifica;

namespace PosTouch.Tests
{
    public static class TestStampe
    {
        public static void Esegui()
        {
            // riepilogo turno: "€" diventa "euro" sulla stampante, la riga non deve superare la carta (prima ",00" andava a capo)
            string rigaEuro = EscPos.DueColonne("Scontrini (1)", "€ 22,00");
            Ok(rigaEuro.Length == EscPos.Colonne && rigaEuro.EndsWith("euro 22,00"), "importo in euro tutto sulla stessa riga");
            Ok(EscPos.DueColonne(new string('X', 60), "€ 1.234,00").EndsWith("euro 1.234,00") && EscPos.DueColonne(new string('X', 60), "€ 1.234,00").Length == EscPos.Colonne,
               "etichetta troppo lunga: si accorcia l'etichetta, mai l'importo");

            var v = new ConfigSale();
            v.Stampanti.Add(new StampanteRete { Id = 1, Nome = "A", IP = "10.0.0.5", Porta = 9100 });
            v.Stampanti.Add(new StampanteRete { Id = 2, Nome = "B", IP = "10.0.0.6", Porta = 9101 });
            v.Centri[0].StampanteId = 1; v.Centri[1].StampanteId = 2;
            v.StampantiSala.Add(new StampanteSala { SalaId = 1, CentroId = 1, StampanteId = 2 });
            var c = ConfigStampe.DaVecchia(v, "10.0.0.9", 9100, true);
            Ok(c.IndirizziIP.SequenceEqual(new[] { "10.0.0.5", "10.0.0.6:9101", "10.0.0.9" }), "IP migrati dalle vecchie stampanti");
            Ok(c.Uscite.Count == 2 && c.NomeUscita(1) == "CUCINA", "centri → uscite");
            Ok(c.ImpostazioniPerSala && c.StampanteUscita(1, 1).IP == "10.0.0.6", "eccezione sala → pagina della sala");
            Uguale("10.0.0.5", c.StampanteUscita(ConfigStampe.SalaBanco, 1)?.IP, "il banco usa la base");
            Uguale("10.0.0.9", c.Base.Preconto.IP, "vecchia termica → preconti");
            Ok(c.StampanteUscita(1, 7) == null, "uscita inesistente: nessuna stampante");
            var copia = c.Copia(); copia.PerSala[1].Uscite[1].IP = "1.1.1.1";
            Uguale("10.0.0.6", c.PerSala[1].Uscite[1].IP, "copia profonda");

            var t = c.Copia();
            t.IndirizziIP.Remove("10.0.0.6:9101");
            int tolti = t.TogliIndirizziAssenti();
            Ok(tolti >= 1 && string.IsNullOrEmpty(t.PerSala[1].Uscite[1].IP), "IP eliminato: la stampa resta senza stampante");
            Uguale("10.0.0.5", t.Base.Uscite[1].IP, "gli altri IP restano");

            // ── Escludi altre / Escludi in altre (per uscita) ──
            var e = new ConfigStampe();
            e.Uscite.Add(new Uscita { Numero = 1, Nome = "CUCINA" });
            e.Uscite.Add(new Uscita { Numero = 2, Nome = "BAR" });
            e.Uscite.Add(new Uscita { Numero = 3, Nome = "PIZZERIA" });
            RigaScontrino P(string d, int c, int portata = 1) => new RigaScontrino { Tipo = TipoRiga.Prodotto, Descrizione = d, Quantita = 1, CentroProduzioneId = c, Portata = portata };
            var righe = new List<RigaScontrino> { P("BRUSCHETTA", 1), P("COCA", 2), P("MARGHERITA", 3) };
            var gruppi = righe.GroupBy(r => r.CentroProduzioneId).ToList();
            Uguale(2, e.AltriPer(1, gruppi).Count, "senza esclusioni la cucina vede bar e pizzeria");
            e.UscitaDa(2).EscludiInAltre = true;
            Ok(!e.AltriPer(1, gruppi).ContainsKey("BAR") && e.AltriPer(1, gruppi).ContainsKey("PIZZERIA"), "escludi in altre: le bibite spariscono dalla cucina");
            Ok(e.AltriPer(2, gruppi).Count == 2, "escludi in altre: il bar vede ancora gli altri");
            e.UscitaDa(2).EscludiAltre = true;
            Ok(e.AltriPer(2, gruppi) == null, "escludi altre: il bar vede solo le sue righe");
            var sep = new RigaScontrino { Tipo = TipoRiga.Portata, Descrizione = "SEGUE", Portata = 2 };
            var flusso = new List<RigaScontrino> { P("BRUSCHETTA", 1), P("COCA", 2), sep, P("BIRRA", 2, 2) };
            var perCucina = e.FlussoPer(1, flusso);
            Ok(perCucina.Count == 1 && perCucina[0].Descrizione == "BRUSCHETTA", "flusso cucina: niente bibite e niente SEGUE rimasto vuoto");
            Uguale(3, e.FlussoPer(2, flusso).Count, "flusso bar (escludi altre): solo bibite e il suo SEGUE");
            var copiaU = e.Copia();
            Ok(copiaU.UscitaDa(2).EscludiAltre && copiaU.UscitaDa(2).EscludiInAltre, "le esclusioni si copiano con le opzioni");
            Ok(e.Firma().Contains("EscludiInAltre"), "le esclusioni si salvano nelle impostazioni");

            // ── listino per sala: si copia e si normalizza ──
            var cs = new ConfigSale();
            cs.Sale.Add(new Sala { Id = 101, Nome = "TERRAZZA", Listino = 3 });
            cs.Sale.Add(new Sala { Id = 102, Nome = "X", Listino = 9 });
            cs.ListinoContiClienti = 0;
            var cs2 = cs.Copia();
            Ok(cs2.SalaDa(101).Listino == 3 && cs2.SalaDa(102).Listino == 4 && cs2.ListinoContiClienti == 1, "listino della sala copiato e limitato a 1-4");

            // ── totale a persona sul conto ──
            var righeConto = new List<RigaScontrino> { new RigaScontrino { Tipo = TipoRiga.Prodotto, Descrizione = "MENU", Quantita = 3, PrezzoCentesimi = 1000 } };
            string conto = System.Text.Encoding.Latin1.GetString(LayoutConto.Crea(righeConto, 1, "PROVA", "", "", true, System.DateTime.Now, coperti: 3));
            Ok(conto.Contains("Totale/Persona") && conto.Contains("10,00"), "conto con coperti: totale a persona 10,00");
            string senza = System.Text.Encoding.Latin1.GetString(LayoutConto.Crea(righeConto, 1, "PROVA", "", "", true, System.DateTime.Now));
            Ok(!senza.Contains("Totale/Persona"), "senza coperti nessun totale a persona");
            Ok(c.Firma().Length > 10, "impronta JSON");

            var corpo = new EscPos().Testo("CIAO").Byte();
            var f = Finitura.Applica(corpo, new ParametriStampa { RigheSaltoIniziale = 2, RigheSaltoFinale = 3, Taglio = 1, Cicalino = 2, Inversione = true }, 1);
            Ok(f[0] == 0x1B && f[1] == 0x40 && f[2] == 0x1B && f[3] == 0x7B && f[5] == 0x0A && f[6] == 0x0A, "inizio: init, capovolta, 2 righe");
            Ok(f[^4] == 0x1D && f[^3] == 0x56 && f[^2] == 0x42, "taglio parziale in fondo");
            Uguale(6, f.Count(b => b == 0x07), "2 beep");
            var (ip, porta) = ConfigStampe.Separa("10.1.1.1:9200");
            Ok(ip == "10.1.1.1" && porta == 9200, "IP:porta separati");
        }
    }
}
