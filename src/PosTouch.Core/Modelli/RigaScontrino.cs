using System.Linq;
using System.ComponentModel;

namespace PosTouch.Core.Modelli
{
    /// <summary>Stessi tipi di riga del PC. Subtotale e Separatore sono solo visivi (blocco sconto).</summary>
    /// <summary>Portata = separatore delle uscite al tavolo ("── SEGUE ──", "⚡⚡ SUBITO ⚡⚡"); non conta nel totale.</summary>
    /// <summary>Gusto = riga figlia di un articolo a combinazione (prezzo nel padre), come sul PC.</summary>
    /// <summary>Variante = riga decorativa sotto un articolo o un gusto ("+ MOZZARELLA"); il prezzo è già nel padre.</summary>
    public enum TipoRiga { Prodotto, Sconto, Subtotale, Separatore, Portata, Gusto, Variante }

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
        /// <summary>Nome della categoria al momento della vendita (per lo storico).</summary>
        public string CategoriaNome { get; set; } = "";
        /// <summary>Aliquota IVA al momento della vendita (per lo storico).</summary>
        public int AliquotaIVA { get; set; }
        /// <summary>Unità di peso ("Kg", "g"...) per gli articoli venduti a peso; null = articolo a pezzi.</summary>
        public string UnitaPeso { get => _unitaPeso; set { if (_unitaPeso != value) { _unitaPeso = value; Notifica(); } } }
        private string _unitaPeso;
        public bool IsAPeso => !string.IsNullOrEmpty(UnitaPeso);
        /// <summary>
        /// Varianti con prezzo su un articolo a peso: importo fisso NON moltiplicato per il peso
        /// (sull'RT esce come riga a parte "VARIANTI", o come sconto sulla riga se negativo).
        /// </summary>
        public int SovrapprezzoCent { get => _sovrapprezzo; set { if (_sovrapprezzo != value) { _sovrapprezzo = value; Notifica(); } } }
        private int _sovrapprezzo;

        // ── Tavoli e comande ──
        /// <summary>Centro di produzione (CUCINA, BAR...) dove va la comanda; 0 = nessuna stampa.</summary>
        public int CentroProduzioneId { get; set; }
        /// <summary>Numero logico della portata (0 = SUBITO / prima uscita). Per le righe Portata è il numero del separatore.</summary>
        public int Portata { get; set; }

        private bool _comandata;
        /// <summary>Già inviata in produzione: toglierla genera uno STORNO.</summary>
        public bool Comandata
        {
            get => _comandata;
            set { if (_comandata != value) { _comandata = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Comandata))); } }
        }
        public int PrezzoOriginaleCentesimi { get; set; }
        public int? ScontoPercentualeCent { get; set; }

        private int _prezzoCentesimi;
        public int PrezzoCentesimi
        {
            get => _prezzoCentesimi;
            set { if (_prezzoCentesimi != value) { _prezzoCentesimi = value; Notifica(); } }
        }

        private decimal _quantita = 1;
        public decimal Quantita
        {
            get => _quantita;
            set { if (_quantita != value) { _quantita = value; Notifica(); } }
        }

        // ── stato visivo della lista (impostato da Carrello / pagina) ──
        private bool _alternata;
        public bool Alternata
        {
            get => _alternata;
            set { if (_alternata != value) { _alternata = value; NotificaSfondo(nameof(Alternata)); } }
        }

        private bool _selezionata;
        public bool Selezionata
        {
            get => _selezionata;
            set { if (_selezionata != value) { _selezionata = value; NotificaSfondo(nameof(Selezionata)); } }
        }

        private bool _portataFutura;
        /// <summary>
        /// Al tavolo con le portate: la riga è in una portata NON ancora avanzata (dopo la portata corrente).
        /// Sfondo arancione chiaro come sul PC (AggiornaColoriPortate). Solo schermo, non si salva.
        /// </summary>
        public bool PortataFutura
        {
            get => _portataFutura;
            set { if (_portataFutura != value) { _portataFutura = value; NotificaSfondo(nameof(PortataFutura)); } }
        }

        /// <summary>Sfondo come sul PC: alternato bianco/#EEEEEE, selezionata azzurra, portata non ancora avanzata arancio chiaro.</summary>
        public string ColoreSfondo => Selezionata ? "#BBDEFB" : PortataFutura ? "#FFF3E0" : (Alternata ? "#EEEEEE" : "#FFFFFF");   // selezionata azzurra: si vede bene cosa cancella C

        /// <summary>Intestazione portata come sul PC: SUBITO rosso chiaro con scritta rossa, le altre viola chiaro.</summary>
        public string ColoreIntestazione => Descrizione != null && Descrizione.Contains("SUBITO") ? "#FFCDD2" : "#E8EAF6";
        public string ColoreTestoIntestazione => Descrizione != null && Descrizione.Contains("SUBITO") ? "#B71C1C" : "#283593";

        public bool IsProdotto => Tipo == TipoRiga.Prodotto;
        public bool IsSconto => Tipo == TipoRiga.Sconto;
        public bool IsSubtotale => Tipo == TipoRiga.Subtotale;
        public bool IsSeparatore => Tipo == TipoRiga.Separatore;
        public bool IsPortata => Tipo == TipoRiga.Portata;
        public bool IsGusto => Tipo == TipoRiga.Gusto;
        public bool IsVariante => Tipo == TipoRiga.Variante;
        /// <summary>Prezzo della variante (con segno) per la lista: "+€0,50" / "-€0,30"; vuoto se gratis.</summary>
        public string VarianteDisplay => Tipo != TipoRiga.Variante || PrezzoCentesimi == 0 ? ""
            : $"{(PrezzoCentesimi > 0 ? "+" : "-")}€{System.Math.Abs(PrezzoCentesimi) / 100m:F2}".Replace('.', ',');

        /// <summary>Testo stampato in comanda sotto l'articolo (gusti della combinazione, separati da "|").</summary>
        public string VariantiTesto { get; set; }
        /// <summary>Conta nel totale (articoli e sconti; subtotale e separatori no).</summary>
        public bool ContaNelTotale => Tipo == TipoRiga.Prodotto || Tipo == TipoRiga.Sconto;

        public decimal ImportoEuro => ImportoCentesimi / 100m;
        public int ImportoCentesimi => (int)System.Math.Round(PrezzoCentesimi * Quantita, System.MidpointRounding.AwayFromZero) + SovrapprezzoCent;
        public decimal PrezzoEuro => PrezzoCentesimi / 100m;

        public string QuantitaDisplay => Tipo != TipoRiga.Prodotto ? "" : IsAPeso ? "⚖" :
            (Quantita == decimal.Truncate(Quantita) ? Quantita.ToString("0") : Quantita.ToString("0.###"));
        /// <summary>Riga a peso: "0,385 Kg × € 12,00/Kg" (+ varianti a importo fisso).</summary>
        public string DettaglioPeso => !IsAPeso ? "" :
            $"{Quantita.ToString("0.000", It)} {UnitaPeso} × € {(PrezzoCentesimi / 100m).ToString("N2", It)}/{UnitaPeso}"
            + (SovrapprezzoCent != 0 ? $"  {(SovrapprezzoCent > 0 ? "+" : "-")}€ {(System.Math.Abs(SovrapprezzoCent) / 100m).ToString("N2", It)}" : "");
        private static readonly System.Globalization.CultureInfo It = System.Globalization.CultureInfo.GetCultureInfo("it-IT");
        public string ImportoDisplay => Tipo == TipoRiga.Separatore ? "" : $"€ {ImportoEuro:F2}".Replace('.', ',');

        private static readonly PropertyChangedEventArgs[] _cambiValori = new[] { nameof(PrezzoCentesimi), nameof(Quantita), nameof(ImportoEuro), nameof(ImportoDisplay),
                                      nameof(QuantitaDisplay), nameof(DettaglioPeso), nameof(IsAPeso), nameof(VarianteDisplay) }
            .Select(n => new PropertyChangedEventArgs(n)).ToArray();
        private static readonly PropertyChangedEventArgs _cambioSfondo = new PropertyChangedEventArgs(nameof(ColoreSfondo));

        /// <summary>Prezzo, quantità, peso, varianti: tutto ciò che si vede della riga tranne lo sfondo.</summary>
        private void Notifica()
        {
            var h = PropertyChanged;
            if (h == null) return;
            foreach (var e in _cambiValori) h(this, e);
        }

        /// <summary>Alternata/selezionata cambiano solo lo sfondo: non si ricalcolano prezzi e importi (prima 11 notifiche).</summary>
        private void NotificaSfondo(string proprieta)
        {
            var h = PropertyChanged;
            if (h == null) return;
            h(this, new PropertyChangedEventArgs(proprieta));
            h(this, _cambioSfondo);
        }

        public RigaScontrino Copia()
        {
            var c = (RigaScontrino)MemberwiseClone();
            c.PropertyChanged = null; // la copia non deve aggiornare la UI
            return c;
        }
    }
}
