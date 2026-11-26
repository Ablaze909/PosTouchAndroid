namespace PosTouch.Core.Modelli
{
    /// <summary>Prodotto (tabella Prodotti di pos.db, con reparto e IVA presi dalla categoria).</summary>
    public class Prodotto
    {
        public int Id { get; set; }
        public string Codice { get; set; } = "";
        public string Barcode { get; set; } = "";
        public string Descrizione { get; set; } = "";
        public int PrezzoCentesimi { get; set; }
        public int CostoCentesimi { get; set; }
        public int CategoriaId { get; set; }
        public string CategoriaNome { get; set; } = "";
        public int RepartoFiscale { get; set; }
        public int AliquotaIVA { get; set; }
        public string Colore { get; set; } = "#4CAF50";
        public string ColoreTesto { get; set; } = "#FFFFFF";
        /// <summary>Dimensione testo del tasto: 10 piccolo, 12 medio, 14 grande, 16 XL (come sul PC).</summary>
        public int DimensioneFontTasto { get; set; } = 12;
        public bool IsPreferito { get; set; }
        /// <summary>Centro di produzione dell'articolo: 0 = come la categoria, -1 = nessuna stampa, &gt;0 = centro.</summary>
        public int CentroProduzioneId { get; set; }
        /// <summary>Centro effettivo per le comande (articolo, altrimenti categoria); 0 = nessuna stampa.</summary>
        public int CentroStampa { get; set; }
        /// <summary>Vendita a peso (dal PC): la quantità è il peso, il prezzo è per unità di peso.</summary>
        public bool VenditaAPeso { get; set; }
        public string UnitaPeso { get; set; } = "Kg";
        /// <summary>Articolo combinazione multi-gusto (dal PC): non ancora gestito su Android.</summary>
        public bool IsCombinazione { get; set; }
        /// <summary>Visibile nel POS (false = nascosto).</summary>
        public bool Attivo { get; set; } = true;

        public decimal PrezzoEuro => PrezzoCentesimi / 100m;
        public decimal CostoEuro => CostoCentesimi / 100m;
        public decimal MargineEuro => PrezzoEuro - CostoEuro;
        public string PrezzoDisplay => $"€ {PrezzoEuro:F2}".Replace('.', ',');
        public string MargineDisplay => CostoCentesimi > 0 ? $"€ {MargineEuro:F2}".Replace('.', ',') : "—";
    }
}
