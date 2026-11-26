using System.ComponentModel;

namespace PosTouch.Core.Modelli
{
    public enum TipoRiga { Prodotto, Sconto }

    /// <summary>
    /// Riga del carrello. Stessa semantica della versione Windows:
    /// PrezzoCentesimi = prezzo unitario; per una riga Sconto è negativo (sconto a valore)
    /// oppure calcolato dalla percentuale (ScontoPercentualeCent, es. 1050 = 10,50%).
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

        private bool _alternata;
        /// <summary>Riga dispari della lista (sfondo alternato come sul PC). Impostata dal Carrello.</summary>
        public bool Alternata
        {
            get => _alternata;
            set
            {
                if (_alternata == value) return;
                _alternata = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Alternata)));
            }
        }

        public bool IsSconto => Tipo == TipoRiga.Sconto;
        public bool IsProdotto => Tipo == TipoRiga.Prodotto;

        /// <summary>Importo totale della riga in euro (prezzo × quantità).</summary>
        public decimal ImportoEuro => PrezzoCentesimi * Quantita / 100m;
        public int ImportoCentesimi => (int)System.Math.Round(PrezzoCentesimi * Quantita, System.MidpointRounding.AwayFromZero);

        public string QuantitaDisplay => Tipo == TipoRiga.Sconto ? "" :
            (Quantita == decimal.Truncate(Quantita) ? Quantita.ToString("0") : Quantita.ToString("0.###"));
        public string ImportoDisplay => $"€ {ImportoEuro:N2}";

        private void Notifica()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PrezzoCentesimi)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantita)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImportoEuro)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImportoDisplay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QuantitaDisplay)));
        }

        public RigaScontrino Copia()
        {
            var c = (RigaScontrino)MemberwiseClone();
            c.PropertyChanged = null; // la copia non deve aggiornare la UI
            return c;
        }
    }
}
