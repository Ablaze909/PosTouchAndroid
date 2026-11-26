namespace PosTouch.Core.Modelli
{
    /// <summary>Categoria prodotti (tabella Categorie di pos.db). Il reparto fiscale sta qui.</summary>
    public class Categoria
    {
        public int Id { get; set; }
        public string Nome { get; set; } = "";
        public int RepartoFiscale { get; set; }
        public int AliquotaIVA { get; set; } = 10;
        public string Colore { get; set; } = "#4CAF50";
        public string ColoreTesto { get; set; } = "#000000";
        public string Icona { get; set; } = "";
        public int Ordine { get; set; }
        /// <summary>Centro di produzione delle comande (0 = nessuna stampa).</summary>
        public int CentroProduzioneId { get; set; }
        /// <summary>Comanda da banco: gli articoli di questa categoria non vanno in comanda (EscludiComandaBanco del PC).</summary>
        public bool EscludiComandaBanco { get; set; }
        /// <summary>⚡ SUBITO automatico (colonna Subito del PC): al tavolo con le portate gli articoli vanno sempre nella portata SUBITO.</summary>
        public bool Subito { get; set; }
        /// <summary>Visibile nel POS (false = nascosta, come sul PC).</summary>
        public bool Attiva { get; set; } = true;
        /// <summary>Numero articoli attivi (calcolato dal catalogo).</summary>
        public int NumeroProdotti { get; set; }

        /// <summary>Testo del tasto: icona + nome, come la barra categorie del PC.</summary>
        public string TestoTasto => string.IsNullOrWhiteSpace(Icona) ? Nome : $"{Icona}\n{Nome}";
    }
}
