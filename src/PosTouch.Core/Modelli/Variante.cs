using System.Collections.Generic;

namespace PosTouch.Core.Modelli
{
    /// <summary>0 = Standard (+/- con prezzo, es. "+MOZZARELLA"), 1 = Preparazione (senza segno, es. "BEN COTTA").</summary>
    public enum TipoVariante { Standard = 0, Preparazione = 1 }

    /// <summary>Variante (tabelle Varianti + CategorieVarianti del pos.db del PC).</summary>
    public class Variante
    {
        public int Id { get; set; }
        public string Nome { get; set; } = "";
        /// <summary>Vuoto = "Generali".</summary>
        public string Gruppo { get; set; } = "";
        public TipoVariante Tipo { get; set; } = TipoVariante.Standard;
        public int PrezzoAggiuntaCent { get; set; }
        public int PrezzoRimozioneCent { get; set; }
        public int Ordine { get; set; }
        public bool Attiva { get; set; } = true;
        /// <summary>Categorie in cui è disponibile.</summary>
        public List<int> Categorie { get; set; } = new List<int>();

        public string GruppoDisplay => string.IsNullOrEmpty(Gruppo) ? "Generali" : Gruppo;
    }
}
