namespace PosTouch.Core.Modelli
{
    /// <summary>Categoria prodotti (tabella Categorie di pos.db). Il reparto fiscale sta qui.</summary>
    public class Categoria
    {
        public int Id { get; set; }
        public string Nome { get; set; } = "";
        public int RepartoFiscale { get; set; }
        public int AliquotaIVA { get; set; }
        public string Colore { get; set; } = "";
        public string ColoreTesto { get; set; } = "#000000";
        public int Ordine { get; set; }
    }
}
