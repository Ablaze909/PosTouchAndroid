using System.ComponentModel;

namespace PosTouch.Core.Modelli
{
    /// <summary>Stessi tipi di riga del PC. Subtotale e Separatore sono solo visivi (blocco sconto).</summary>
    public enum TipoRiga { Prodotto, Sconto, Subtotale, Separatore }

    /// <summary>
    /// Riga del carrello. Stessa semantica della versione Windows:
    /// PrezzoCentesimi = prezzo unitario; per una riga Sconto è negativo.
    /// ScontoPercentualeCent: percentuale × 100 (es. 1050 = 10,50%) per gli sconti percentuali.
    /// </summary>
    public class RigaScontrino : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public TipoRiga Tipo { get; set; } = TipoRiga.Prodotto;
        public int? ProdottoId { get; set; }
        public string Descrizione { get; set; } = "";
        public string Barcode { get; set; } = "";
        public int RepartoFiscale { get; set; }
        public int CategoriaId { get; set; }
        public int PrezzoOriginaleCentesimi { get; set; }
        public int? ScontoPercentualeCent { get; set; }

        private int _prezzoCentesimi;
        public int PrezzoCentesimi
        {
            get => _prezzoCentesimi;
            set { _prezzoCentesimi = value; Notifica(); }
        }

        private decimal _quantita = 1;
        public decimal Quantita
        {
            get => _quantita;
            set { _quantita = value; Notifica(); }
        }

        // ── stato visivo della lista (impostato da Carrello / pagina) ──
        private bool _alternata;
        public bool Alternata
        {
            get => _alternata;
            set { if (_alternata != value) { _alternata = value; Notifica(); } }
        }

        private bool _selezionata;
        public bool Selezionata
        {
            get => _selezionata;
            set { if (_selezionata != value) { _selezionata = value; Notifica(); } }
        }

        /// <summary>Sfondo come sul PC: alternato bianco/#EEEEEE, selezionata #E0E0E0.</summary>
        public string ColoreSfondo => Selezionata ? "#E0E0E0" : (Alternata ? "#EEEEEE" : "#FFFFFF");

        public bool IsProdotto => Tipo == TipoRiga.Prodotto;
        public bool IsSconto => Tipo == TipoRiga.Sconto;
        public bool IsSubtotale => Tipo == TipoRiga.Subtotale;
        public bool IsSeparatore => Tipo == TipoRiga.Separatore;
        /// <summary>Conta nel totale (articoli e sconti; subtotale e separatori no).</summary>
        public bool ContaNelTotale => Tipo == TipoRiga.Prodotto || Tipo == TipoRiga.Sconto;

        public decimal ImportoEuro => PrezzoCentesimi * Quantita / 100m;
        public int ImportoCentesimi => (int)System.Math.Round(PrezzoCentesimi * Quantita, System.MidpointRounding.AwayFromZero);
        public decimal PrezzoEuro => PrezzoCentesimi / 100m;

        public string QuantitaDisplay => Tipo != TipoRiga.Prodotto ? "" :
            (Quantita == decimal.Truncate(Quantita) ? Quantita.ToString("0") : Quantita.ToString("0.###"));
        public string ImportoDisplay => Tipo == TipoRiga.Separatore ? "" : $"€ {ImportoEuro:F2}".Replace('.', ',');

        private void Notifica()
        {
            foreach (var p in new[] { nameof(PrezzoCentesimi), nameof(Quantita), nameof(ImportoEuro), nameof(ImportoDisplay),
                                      nameof(QuantitaDisplay), nameof(Alternata), nameof(Selezionata), nameof(ColoreSfondo) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }

        public RigaScontrino Copia()
        {
            var c = (RigaScontrino)MemberwiseClone();
            c.PropertyChanged = null; // la copia non deve aggiornare la UI
            return c;
        }
    }
}
