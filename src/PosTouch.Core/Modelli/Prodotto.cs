namespace PosTouch.Core.Modelli
{
    /// <summary>Prodotto vendibile (tabella Prodotti di pos.db, con reparto preso dalla categoria).</summary>
    public class Prodotto
    {
        public int Id { get; set; }
        public string Codice { get; set; } = "";
        public string Barcode { get; set; } = "";
        public string Descrizione { get; set; } = "";
        public int PrezzoCentesimi { get; set; }
        public int CategoriaId { get; set; }
        public int RepartoFiscale { get; set; }
        public string Colore { get; set; } = "";
        public string ColoreTesto { get; set; } = "#FFFFFF";
        public bool IsPreferito { get; set; }

        public decimal PrezzoEuro => PrezzoCentesimi / 100m;
    }
}
