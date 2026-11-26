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
        /// <summary>Ordine dei tasti (come il PC: Ordine, poi descrizione).</summary>
        public int Ordine { get; set; }
        /// <summary>Centro di produzione dell'articolo: 0 = come la categoria, -1 = nessuna stampa, &gt;0 = centro.</summary>
        public int CentroProduzioneId { get; set; }
        /// <summary>Centro effettivo per le comande (articolo, altrimenti categoria); 0 = nessuna stampa.</summary>
        public int CentroStampa { get; set; }
        /// <summary>Prezzi dei listini 2, 3 e 4 (colonne Prezzo2/3/4Centesimi del PC). 0 = come il listino 1.</summary>
        public int Prezzo2Centesimi { get; set; }
        public int Prezzo3Centesimi { get; set; }
        public int Prezzo4Centesimi { get; set; }

        /// <summary>GetPrezzoCentesimi del PC: prezzo del listino 1-4, con ripiego sul listino 1 se vuoto.</summary>
        public int PrezzoListino(int listino)
        {
            int p = listino switch { 2 => Prezzo2Centesimi, 3 => Prezzo3Centesimi, 4 => Prezzo4Centesimi, _ => PrezzoCentesimi };
            return p > 0 ? p : PrezzoCentesimi;
        }

        public string PrezzoDisplayListino(int listino) => $"€ {PrezzoListino(listino) / 100m:F2}".Replace('.', ',');
        /// <summary>⚡ SUBITO automatico ereditato dalla categoria (calcolato dal catalogo).</summary>
        public bool Subito { get; set; }
        /// <summary>Vendita a peso (dal PC): la quantità è il peso, il prezzo è per unità di peso.</summary>
        public bool VenditaAPeso { get; set; }
        public string UnitaPeso { get; set; } = "Kg";
        /// <summary>Articolo combinazione multi-gusto (pizza ½+½ ecc.).</summary>
        public bool IsCombinazione { get; set; }
        /// <summary>Articolo COPERTO (colonna IsCoperto del PC): si sceglie come coperto delle sale.</summary>
        public bool IsCoperto { get; set; }
        /// <summary>Combinazione: quanti gusti (2 = ½+½, 3, 4...).</summary>
        public int NumeroGusti { get; set; } = 2;
        /// <summary>Combinazione: 0 = prezzo del gusto più caro, 1 = media dei gusti, 2 = prezzo fisso dell'articolo.</summary>
        public int PoliticaPrezzo { get; set; }
        /// <summary>Combinazione: il prezzo delle varianti si divide per il numero di gusti.</summary>
        public bool VariantiProporzionali { get; set; }
        /// <summary>Codice bilancia: il barcode (5 cifre) viene cercato nelle etichette EAN-13 della bilancia.</summary>
        public bool IsCodiceBilancia { get; set; }
        /// <summary>Visibile nel POS (false = nascosto).</summary>
        public bool Attivo { get; set; } = true;

        public decimal PrezzoEuro => PrezzoCentesimi / 100m;
        public decimal CostoEuro => CostoCentesimi / 100m;
        public decimal MargineEuro => PrezzoEuro - CostoEuro;
        public string PrezzoDisplay => $"€ {PrezzoEuro:F2}".Replace('.', ',');
        public string MargineDisplay => CostoCentesimi > 0 ? $"€ {MargineEuro:F2}".Replace('.', ',') : "—";
    }
}
