using System.Globalization;
using PosTouch.App.Controlli;

namespace PosTouch.App.Dialoghi
{
    public enum ModoTastierino { Euro, Quantita, Codice }

    /// <summary>
    /// Tastierino numerico (coperti, quantità, prezzi, codici, PIN) nello stile comune delle finestre (StileFinestre).
    /// Euro: "€ 0,00"; Quantita: numero; Codice: cifre (reparto, barcode, password).
    /// Restituisce il testo digitato (null = annullato).
    /// </summary>
    public class TastierinoPrezzo : DialogoBase<string>
    {
        private readonly ModoTastierino _modo;
        private readonly Label _display;
        private readonly bool _nascosto;
        private string _input = "";
        /// <summary>Valore attuale (invariante): OK senza digitare lo conferma invece di restituire 0.</summary>
        private string _attualeNumero;

        public TastierinoPrezzo(string titolo, string valoreAttuale, ModoTastierino modo, bool nascondi = false)
        {
            _modo = modo;
            _nascosto = nascondi;
            var (tit, dettaglio) = StileFinestre.Dividi(titolo);
            _display = new Label { LineBreakMode = LineBreakMode.NoWrap };
            var nota = string.IsNullOrEmpty(valoreAttuale) ? null : new Label { Text = $"attuale: {valoreAttuale}" };

            // in basso a sinistra: "00" per i codici, la virgola per importi e quantità; a destra cancella l'ultima cifra
            string speciale = modo == ModoTastierino.Codice ? "00" : ",";
            var sinistra = StileFinestre.TastoCifra(speciale, (s, e) => Premi(speciale));
            var cancella = StileFinestre.TastoIcona(StileFinestre.IcoCancella, "Cancella", (s, e) =>
            {
                if (_input.Length > 0) _input = _input[..^1];
                Aggiorna();
            });
            var tastiera = StileFinestre.Tastierino(Premi, sinistra, cancella, _tasti);

            var corpo = new VerticalStackLayout { Spacing = 12 };
            corpo.Add(StileFinestre.Intestazione(StileFinestre.IconaPer(tit), tit, dettaglio, () => Chiudi(null)));
            corpo.Add(StileFinestre.Campo(_display, nota));
            corpo.Add(tastiera);
            corpo.Add(StileFinestre.Azioni(StileFinestre.TastoSecondario("ANNULLA", (s, e) => Chiudi(null)),
                                           StileFinestre.TastoPrincipale("OK", (s, e) => Conferma())));
            var riquadro = StileFinestre.Scheda(new ScrollView { Content = corpo });
            Content = riquadro;
            Dimensiona(riquadro, 340);
            Aggiorna();
        }

        private readonly List<Button> _tasti = new List<Button>();

        /// <summary>Schermi bassi (8"): i tasti si accorciano perché ANNULLA/OK restino in vista.</summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (height <= 0) return;
            double tasto = Math.Clamp((height - 24 - 270) / 4, 42, 54);
            foreach (var b in _tasti) if (Math.Abs(b.HeightRequest - tasto) > 0.5) b.HeightRequest = tasto;
        }

        private void Premi(string t)
        {
            if (_modo == ModoTastierino.Codice)
            {
                if (_input.Length + t.Length <= 20) _input += t;   // barcode, reparto, password
                Aggiorna();
                return;
            }
            // Importi e quantità: max 5 cifre intere (99.999), 2 decimali per gli euro, 3 per i pesi
            int virgola = _input.IndexOf(',');
            int maxDecimali = _modo == ModoTastierino.Euro ? 2 : 3;
            if (t == ",")
            {
                if (virgola < 0) _input += _input.Length == 0 ? "0," : ",";
            }
            else if (virgola < 0 ? _input.Length < (Implicita ? 7 : 5) : _input.Length - virgola - 1 < maxDecimali)
            {
                _input = _input == "0" ? t : _input + t;
            }
            Aggiorna();
        }

        private void Aggiorna()
        {
            if (_modo == ModoTastierino.Euro)
                _display.Text = $"€ {Valore():N2}";
            else if (_nascosto)
                _display.Text = _input.Length == 0 ? "–" : new string('●', _input.Length);
            else
                _display.Text = _input.Length == 0 ? "0" : _input;
        }

        /// <summary>Virgola implicita (impostazione): negli importi in euro le cifre sono centesimi finché non si preme la virgola.</summary>
        private bool Implicita => _modo == ModoTastierino.Euro && PosTouch.App.Servizi.ContestoCassa.Impostazioni.VirgolaImplicita;

        private decimal Valore()
        {
            if (!decimal.TryParse(_input.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)) return 0;
            return Implicita && !_input.Contains(',') ? v / 100m : v;
        }

        private void Conferma()
        {
            if (_modo == ModoTastierino.Codice) Chiudi(_input);
            else if (_input.Length == 0 && _attualeNumero != null) Chiudi(_attualeNumero);   // OK a vuoto = tieni il valore attuale (per 0 si digita 0)
            else Chiudi(Valore().ToString(CultureInfo.InvariantCulture));
        }

        // ── scorciatoie ──

        /// <summary>Prezzo in euro (null = annullato).</summary>
        public static async Task<decimal?> ChiediEuro(string titolo, decimal? attuale = null)
        {
            var r = await Dialogo.Mostra(new TastierinoPrezzo(titolo, attuale.HasValue ? $"€ {attuale.Value:N2}" : null, ModoTastierino.Euro)
                { _attualeNumero = attuale?.ToString(CultureInfo.InvariantCulture) });
            return r == null ? null : decimal.Parse(r, CultureInfo.InvariantCulture);
        }

        public static async Task<decimal?> ChiediQuantita(string titolo, decimal? attuale = null)
        {
            var r = await Dialogo.Mostra(new TastierinoPrezzo(titolo, attuale?.ToString("0.###"), ModoTastierino.Quantita)
                { _attualeNumero = attuale?.ToString(CultureInfo.InvariantCulture) });
            return r == null ? null : decimal.Parse(r, CultureInfo.InvariantCulture);
        }

        public static Task<string> ChiediCodice(string titolo, string attuale = null, bool nascondi = false)
            => Dialogo.Mostra(new TastierinoPrezzo(titolo, attuale, ModoTastierino.Codice, nascondi));
    }
}
