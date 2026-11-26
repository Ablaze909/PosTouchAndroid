using System;
using System.Globalization;

namespace PosTouch.Core.Vendita
{
    public enum EsitoTastoEuro { Impostato, Vuoto, NonValido }

    /// <summary>
    /// Tastiera numerica della vendita, stessa logica di MainWindow (PC):
    /// - cifre → quantità del prossimo articolo ("Qta: 3")
    /// - X → blocca la quantità ("Qta: 3 ×"), poi si può digitare il prezzo
    /// - € → il numero digitato diventa il prezzo del prossimo articolo ("Qta: 1 × € 1,50")
    /// - C → azzera
    /// </summary>
    public class Tastierino
    {
        private string _input = "";

        public decimal QuantitaCorrente { get; private set; } = 1;
        public bool QuantitaBloccata { get; private set; }
        public decimal? PrezzoPersonalizzato { get; private set; }
        /// <summary>Impostazione "Blocco modifica quantità": le cifre valgono solo come prezzo.</summary>
        public bool BloccoModificaQuantita { get; set; }

        public string Display { get; private set; } = "";
        /// <summary>Colore display: nero normale, arancio dopo X, verde con prezzo €.</summary>
        public string ColoreDisplay { get; private set; } = "#666666";

        /// <summary>true se c'è qualcosa digitato o impostato (C allora azzera invece di cancellare righe).</summary>
        public bool Attivo => _input.Length > 0 || PrezzoPersonalizzato.HasValue || QuantitaBloccata;
        public string Input => _input;

        public event Action Cambiato;

        public void Premi(string tasto)
        {
            // Limiti: 5 cifre intere (max 99.999) e 3 decimali (pesi): oltre, il tasto viene ignorato.
            // Evita numeri enormi che manderebbero in errore i calcoli (e l'app).
            int virgola = _input.IndexOf(',');
            if (tasto == "," || tasto == "." || tasto == "•")
            {
                if (virgola < 0) _input += _input.Length == 0 ? "0," : ",";
            }
            else if (tasto.Length == 1 && char.IsDigit(tasto[0]))
            {
                bool spazio = virgola < 0 ? _input.Length < 5 : _input.Length - virgola - 1 < 3;
                if (spazio) _input = _input == "0" ? tasto : _input + tasto;
            }

            if (ProvaNumero(_input, out decimal q))
            {
                if (!QuantitaBloccata && !BloccoModificaQuantita)
                    QuantitaCorrente = q;

                if (PrezzoPersonalizzato.HasValue)
                    Mostra($"Qta: {Num(QuantitaCorrente)} × € {Euro(PrezzoPersonalizzato.Value)}", "#2E7D32");
                else if (QuantitaBloccata)
                    Mostra($"Qta: {Num(QuantitaCorrente)} × € {Euro(q)}", "#FF9800");
                else if (BloccoModificaQuantita)
                    Mostra($"€ {Euro(q)}", "#111111");
                else
                    // Sul PC "12" o "1,5" venivano mostrati come "€ 12,00" ma usati come quantità:
                    // qui il display dice quello che succede davvero.
                    Mostra($"Qta: {Num(q)}", "#111111");
            }
            else Cambiato?.Invoke();
        }

        /// <summary>Tasto X: la quantità digitata resta fissa per il prossimo articolo.</summary>
        public bool Moltiplica()
        {
            if (!ProvaNumero(_input, out decimal q) || q <= 0) return false;
            QuantitaCorrente = q;
            QuantitaBloccata = true;
            _input = "";
            Mostra($"Qta: {Num(QuantitaCorrente)} ×", "#FF9800");
            return true;
        }

        /// <summary>Tasto €: il numero digitato diventa il prezzo del prossimo articolo.</summary>
        public EsitoTastoEuro Euro()
        {
            if (_input.Length == 0) return EsitoTastoEuro.Vuoto;
            if (!ProvaNumero(_input, out decimal prezzo) || prezzo <= 0 || Cent(prezzo) <= 0) return EsitoTastoEuro.NonValido;
            PrezzoPersonalizzato = prezzo;
            _input = "";
            if (!QuantitaBloccata) QuantitaCorrente = 1;
            Mostra($"Qta: {Num(QuantitaCorrente)} × € {Euro(prezzo)}", "#2E7D32");
            return EsitoTastoEuro.Impostato;
        }

        /// <summary>Quantità e prezzo da usare per l'articolo toccato; poi la tastiera si azzera (ResetQuantita).</summary>
        public (decimal quantita, int? prezzoCent) PrendiPerArticolo()
        {
            decimal qta = QuantitaCorrente > 0 ? QuantitaCorrente : 1;
            int? prezzo = null;
            if (PrezzoPersonalizzato.HasValue)
                prezzo = Cent(PrezzoPersonalizzato.Value);
            else if (QuantitaBloccata && ProvaNumero(_input, out decimal p) && p > 0)
                prezzo = Cent(p);            // "3 X 1,50" e poi l'articolo
            else if (BloccoModificaQuantita && ProvaNumero(_input, out decimal pb) && pb > 0)
                prezzo = Cent(pb);
            if (BloccoModificaQuantita) qta = 1;
            Azzera();
            return (qta, prezzo);
        }

        /// <summary>ResetQuantita del PC.</summary>
        public void Azzera()
        {
            _input = "";
            QuantitaCorrente = 1;
            PrezzoPersonalizzato = null;
            QuantitaBloccata = false;
            Mostra("", "#111111");
        }

        private void Mostra(string testo, string colore)
        {
            Display = testo;
            ColoreDisplay = colore;
            Cambiato?.Invoke();
        }

        private static int Cent(decimal euro) => (int)Math.Round(euro * 100, MidpointRounding.AwayFromZero);
        private static string Euro(decimal v) => v.ToString("F2", CultureInfo.InvariantCulture).Replace('.', ',');
        private static string Num(decimal v) => v.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', ',');

        private static bool ProvaNumero(string s, out decimal valore) =>
            decimal.TryParse((s ?? "").Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valore);
    }
}
