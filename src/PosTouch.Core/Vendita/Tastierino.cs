using System;
using System.Globalization;

namespace PosTouch.Core.Vendita
{
    /// <summary>
    /// Stato del tastierino numerico della vendita, come sul PC:
    /// - cifre digitate poi prodotto  → quantità
    /// - cifre, X, poi prodotto/reparto → quantità bloccata (poi si può digitare il prezzo)
    /// - cifre poi tasto reparto, €, CONTANTI, sconto → importo
    /// </summary>
    public class Tastierino
    {
        private string _testo = "";

        /// <summary>Quantità fissata con il tasto X (null = nessuna).</summary>
        public decimal? QuantitaBloccata { get; private set; }

        public string Testo => _testo;
        public bool Vuoto => _testo.Length == 0;

        public event Action Cambiato;

        /// <summary>Testo da mostrare sul display.</summary>
        public string Display
        {
            get
            {
                string q = QuantitaBloccata.HasValue ? $"{QuantitaBloccata.Value:0.###} ×  " : "";
                return q + (_testo.Length > 0 ? _testo : (q.Length > 0 ? "" : "0"));
            }
        }

        public void Premi(string tasto)
        {
            if (tasto == "," || tasto == ".")
            {
                if (!_testo.Contains(",")) _testo = (_testo.Length == 0 ? "0" : _testo) + ",";
            }
            else if (_testo.Length < 9)
            {
                _testo += tasto;
            }
            Cambiato?.Invoke();
        }

        /// <summary>Tasto C: azzera tutto.</summary>
        public void Cancella()
        {
            _testo = "";
            QuantitaBloccata = null;
            Cambiato?.Invoke();
        }

        /// <summary>Tasto X: il numero digitato diventa la quantità del prossimo articolo.</summary>
        public bool Moltiplica()
        {
            if (!ProvaNumero(out decimal q) || q <= 0) return false;
            QuantitaBloccata = q;
            _testo = "";
            Cambiato?.Invoke();
            return true;
        }

        /// <summary>Quantità per un articolo a prezzo fisso: quella bloccata, altrimenti il numero digitato, altrimenti 1.</summary>
        public decimal PrendiQuantita()
        {
            decimal q = 1;
            if (QuantitaBloccata.HasValue) q = QuantitaBloccata.Value;
            else if (ProvaNumero(out decimal digitata) && digitata > 0) q = digitata;
            Cancella();
            return q;
        }

        /// <summary>Importo digitato in centesimi (false se non c'è). Non consuma la quantità bloccata.</summary>
        public bool PrendiImporto(out int centesimi)
        {
            centesimi = 0;
            if (!ProvaNumero(out decimal euro) || euro <= 0) return false;
            centesimi = (int)Math.Round(euro * 100, MidpointRounding.AwayFromZero);
            _testo = "";
            Cambiato?.Invoke();
            return true;
        }

        /// <summary>Numero digitato come valore (es. percentuale sconto).</summary>
        public bool PrendiValore(out decimal valore)
        {
            if (!ProvaNumero(out valore) || valore <= 0) return false;
            _testo = "";
            Cambiato?.Invoke();
            return true;
        }

        /// <summary>Quantità bloccata (o 1) per un articolo di cui si sta battendo il prezzo; poi azzera.</summary>
        public decimal PrendiQuantitaBloccata()
        {
            decimal q = QuantitaBloccata ?? 1;
            Cancella();
            return q;
        }

        private bool ProvaNumero(out decimal valore) =>
            decimal.TryParse(_testo.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valore);
    }
}
