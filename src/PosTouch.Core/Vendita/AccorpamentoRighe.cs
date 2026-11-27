using System.Collections.Generic;
using System.Linq;
using PosTouch.Core.Modelli;

namespace PosTouch.Core.Vendita
{
    /// <summary>
    /// Raggruppa righe identiche (stesso reparto + descrizione + prezzo) sommando le quantità.
    /// Stessa regola di MainWindow.AccorpaRigheFiscali. Le righe originali non vengono modificate.
    /// </summary>
    public static class AccorpamentoRighe
    {
        public static List<RigaScontrino> Accorpa(IEnumerable<RigaScontrino> righe)
        {
            var risultato = new List<RigaScontrino>();
            foreach (var r in righe)
            {
                // come il PC: righe a peso mai accorpate (i centesimi arrotondati per riga non tornerebbero);
                // lo stesso per le quantità con decimali (2 × 0,5 a € 7,99 = 8,00 in cassa ma 7,99 accorpate sull'RT)
                bool intera = r.Quantita == decimal.Truncate(r.Quantita);
                var esistente = r.IsAPeso || !intera ? null : risultato.FirstOrDefault(x => !x.IsAPeso && x.Quantita == decimal.Truncate(x.Quantita) &&
                    x.RepartoFiscale == r.RepartoFiscale &&
                    x.Descrizione == r.Descrizione &&
                    x.PrezzoCentesimi == r.PrezzoCentesimi &&
                    (x.VariantiTesto ?? "") == (r.VariantiTesto ?? ""));   // varianti diverse = righe diverse
                if (esistente != null)
                    esistente.Quantita += r.Quantita;
                else
                    risultato.Add(r.Copia());
            }
            return risultato;
        }
    }
}
