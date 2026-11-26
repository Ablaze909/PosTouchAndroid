using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Sale
{
    /// <summary>
    /// Logica delle portate copiata dal PC (VenditaContoClienteDialog):
    /// numero LOGICO del separatore = posizione nella lista configurata (SEGUE=1, SEGUE 2=2...),
    /// con ripiego sul numero finale del nome; "SUBITO" = portata 0. Mai il conteggio per posizione.
    /// </summary>
    public static class Portate
    {
        public const string NomeSubito = "⚡⚡ SUBITO ⚡⚡";

        public static bool IsSubito(RigaScontrino r) => r.Tipo == TipoRiga.Portata && r.Descrizione.Contains("SUBITO");
        public static bool IsSeparatoreReale(RigaScontrino r) => r.Tipo == TipoRiga.Portata && !r.Descrizione.Contains("SUBITO");

        /// <summary>Nome pulito del separatore ("── SEGUE 2 ──" → "SEGUE 2").</summary>
        public static string Nome(RigaScontrino sep) => sep.Descrizione.Replace("──", "").Trim();

        public static string Descrizione(string nome) => $"── {nome.Trim().ToUpperInvariant()} ──";

        public static int NumeroLogico(RigaScontrino sep, IList<string> separatori)
        {
            if (!IsSeparatoreReale(sep)) return 0;
            string nome = Nome(sep);
            if (separatori != null)
                for (int i = 0; i < separatori.Count; i++)
                    if (string.Equals(separatori[i].Trim(), nome, StringComparison.OrdinalIgnoreCase))
                        return i + 1;
            var m = Regex.Match(nome, @"(\d+)\s*$");
            return m.Success ? int.Parse(m.Groups[1].Value) : 1;
        }

        /// <summary>AssegnaPortateARighe del PC: ogni articolo prende la portata del separatore che lo precede.</summary>
        public static void Assegna(IList<RigaScontrino> righe, IList<string> separatori)
        {
            int corrente = 0;
            foreach (var r in righe)
            {
                if (r.Tipo == TipoRiga.Portata)
                {
                    if (IsSubito(r)) { r.Portata = 0; corrente = 0; }   // dopo SUBITO gli articoli sono portata 0
                    else { corrente = NumeroLogico(r, separatori); r.Portata = corrente; }
                }
                else if (r.Tipo == TipoRiga.Prodotto)
                {
                    r.Portata = corrente;
                }
            }
        }

        /// <summary>
        /// Riordino alla chiusura del tavolo (come OnClosing del PC): SUBITO in testa, poi le sezioni
        /// in ordine di numero logico. Durante la sessione la lista resta nell'ordine di inserimento.
        /// </summary>
        public static List<RigaScontrino> Riordina(IList<RigaScontrino> articoliEPortate, IList<string> separatori)
        {
            if (articoliEPortate.Count(IsSeparatoreReale) < 2) return articoliEPortate.ToList();
            var testa = new List<RigaScontrino>();
            var sezioni = new List<(int ordine, int pos, List<RigaScontrino> righe)>();
            var corrente = testa;
            foreach (var r in articoliEPortate)
            {
                if (IsSeparatoreReale(r))
                {
                    corrente = new List<RigaScontrino> { r };
                    sezioni.Add((NumeroLogico(r, separatori), sezioni.Count, corrente));
                }
                else corrente.Add(r);
            }
            var ris = new List<RigaScontrino>(testa);
            foreach (var s in sezioni.OrderBy(x => x.ordine).ThenBy(x => x.pos)) ris.AddRange(s.righe);
            return ris;
        }
    }
}
