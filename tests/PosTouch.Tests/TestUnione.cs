using System.Collections.Generic;
using System.Linq;
using PosTouch.Core.Configurazione;
using PosTouch.Core.Modelli;
using PosTouch.Core.Sale;
using static PosTouch.Tests.Verifica;

namespace PosTouch.Tests
{
    /// <summary>UNISCI per portata e password assistenza.</summary>
    public static class TestUnione
    {
        private static RigaScontrino Art(string d, bool comandata = false) => new RigaScontrino { Tipo = TipoRiga.Prodotto, Descrizione = d, Quantita = 1, PrezzoCentesimi = 100, Comandata = comandata };
        private static RigaScontrino Sep(string d) => new RigaScontrino { Tipo = TipoRiga.Portata, Descrizione = $"── {d} ──" };

        public static void Esegui()
        {
            var sep = new List<string> { "SEGUE 2", "SEGUE 3" };
            // destinazione: acqua (testa), SEGUE 2 con la carne
            var dest = new List<RigaScontrino> { Art("ACQUA", true), Sep("SEGUE 2"), Art("CARNE", true) };
            // unito: birra già fatta in testa, SEGUE 2 con pesce (+ variante), SEGUE 3 dolce
            var var = new RigaScontrino { Tipo = TipoRiga.Variante, Descrizione = "+ LIMONE" };
            var src = new List<RigaScontrino> { Art("BIRRA", true), Sep("SEGUE 2"), Art("PESCE"), var, Sep("SEGUE 3"), Art("DOLCE") };
            Portate.Assegna(dest, sep); Portate.Assegna(src, sep);
            var u = UnioneTavoli.Unisci(new IList<RigaScontrino>[] { dest, src }, sep);
            Portate.Assegna(u, sep);
            string ordine = string.Join(",", u.Select(r => r.Tipo == TipoRiga.Portata ? Portate.Nome(r) : r.Descrizione));
            Uguale("ACQUA,BIRRA,SEGUE 2,CARNE,PESCE,+ LIMONE,SEGUE 3,DOLCE", ordine, "ogni articolo nella sua portata, un solo SEGUE 2");
            Ok(u.First(r => r.Descrizione == "BIRRA").Portata == 0, "la birra già fatta resta in testa (non va in SEGUE 2)");
            Ok(u.First(r => r.Descrizione == "DOLCE").Portata > u.First(r => r.Descrizione == "PESCE").Portata
               && u.First(r => r.Descrizione == "PESCE").Portata == u.First(r => r.Descrizione == "CARNE").Portata, "pesce con la carne, il dolce dopo");
            Uguale(6, u.Count(r => r.Tipo == TipoRiga.Prodotto) + u.Count(r => r.Tipo == TipoRiga.Variante), "nessuna riga persa");

            // password assistenza
            var cfg = new Impostazioni();
            Ok(!PasswordAssistenza.Verifica(cfg, "123456"), "non impostata: non vale niente");
            PasswordAssistenza.Imposta(cfg, "987654");
            Ok(PasswordAssistenza.Verifica(cfg, "987654"), "password assistenza giusta");
            Ok(!PasswordAssistenza.Verifica(cfg, "987655"), "password assistenza sbagliata");
            Ok(!cfg.PasswordAssistenzaHash.Contains("987654"), "salvata solo come hash");
            bool corta = false;
            try { PasswordAssistenza.Imposta(cfg, "123"); } catch (System.ArgumentException) { corta = true; }
            Ok(corta && PasswordAssistenza.Verifica(cfg, "987654"), "troppo corta: rifiutata, resta la vecchia");
            cfg.PasswordAssistenzaSale = "rovinato!";
            Ok(!PasswordAssistenza.Verifica(cfg, "987654"), "impostazioni rovinate: non apre");
            PasswordAssistenza.Imposta(cfg, "");
            Ok(!PasswordAssistenza.Impostata(cfg), "tolta");
        }
    }
}
