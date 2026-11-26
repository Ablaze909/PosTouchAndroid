using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using PosTouch.App.Controlli;
using PosTouch.Core.Modelli;

namespace PosTouch.App.Dialoghi
{
    public partial class CalcoloResto
    {
        // ══════════════ logica (identica al PC) ══════════════

        private void ScegliMetodo(bool carta)
        {
            _modoCarta = carta;
            _input = "";
            Aggiorna();
        }

        private void Taglio(decimal valore)
        {
            if (_cartaImporto > 0 || _contantiRicevuti + valore > MaxContanti) return;
            _contantiRicevuti += valore;
            _modoCarta = false;
            Aggiorna();
        }

        private void Cifra(string t)
        {
            if (_cartaImporto > 0 && !_modoCarta) return;
            string prima = _input;
            if (t == ",") { if (!_input.Contains(",")) _input += _input.Length == 0 ? "0," : ","; }
            else if (t == "00") { if (_input.Length > 0 && _input != "0") _input += "00"; }
            else _input = _input == "0" && t != "0" ? t : _input + t;
            int virgola = _input.IndexOf(',');
            if (virgola >= 0 && _input.Length - virgola > 3) _input = _input.Substring(0, virgola + 3);
            if ((virgola < 0 ? _input.Length : virgola) > 5) _input = prima;   // max € 99.999,99
            AggiornaImporto();
        }

        private void Backspace()
        {
            if (_cartaImporto > 0 && !_modoCarta) return;
            if (_input.Length > 0) _input = _input[..^1];
            AggiornaImporto();
        }

        private void Cancella()
        {
            if (_modoCarta) _cartaImporto = 0;
            else if (_cartaImporto > 0) { _cartaImporto = 0; _contantiRicevuti = 0; }
            else _contantiRicevuti = 0;
            _input = "";
            Aggiorna();
        }

        private void ConfermaImporto()
        {
            decimal importo = Valore();
            if (importo <= 0) return;
            if (_modoCarta)
            {
                _cartaImporto = Math.Min(importo, _totale);
                _input = "";
                _modoCarta = false;
            }
            else
            {
                if (_cartaImporto > 0 || _contantiRicevuti + importo > MaxContanti) return;
                _contantiRicevuti += importo;
                _input = "";
            }
            Aggiorna();
        }

        private void Scontrino()
        {
            if (_input.Length > 0 && Valore() <= 0) _input = "";   // "0" o "0,": niente da confermare
            if (_input.Length > 0)
            {
                ConfermaImporto();   // importo digitato senza ✓: vale come confermato
                if (_input.Length > 0)
                {
                    // rifiutato (oltre il massimo contanti o carta già inserita): non si emette ignorando la cifra
                    _ = MessaggioBox.Avviso("IMPORTO NON ACCETTATO", "L'importo digitato non è stato accettato.\n\nCorreggilo o cancellalo con C prima di emettere.");
                    return;
                }
            }
            if (!PagamentoCompleto) return;
            int carta = Cent(_cartaImporto);
            int contanti = Cent(ParteContanti);
            Chiudi(new EsitoPagamento
            {
                Metodo = carta > 0 && contanti > 0 ? MetodiPagamento.Misto : carta > 0 ? MetodiPagamento.Carta : MetodiPagamento.Contanti,
                CartaCent = carta,
                ContantiCent = contanti,
                RicevutoCent = Cent(_contantiRicevuti),
                RestoCent = Cent(Resto)
            });
        }

        private decimal Valore() =>
            decimal.TryParse(_input.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) ? v : 0;

        private static int Cent(decimal euro) => (int)Math.Round(euro * 100, MidpointRounding.AwayFromZero);

        private void AggiornaImporto()
        {
            decimal v = Valore();
            _importo.Text = v > 0 ? $"{v:N2}" : "0,00";
        }

        private void Aggiorna()
        {
            AggiornaImporto();
            _voceContanti.BackgroundColor = Ui.C(_modoCarta ? "#FFFFFF" : "#BBDEFB");
            _voceCarta.BackgroundColor = Ui.C(_modoCarta ? "#BBDEFB" : "#FFFFFF");

            if (PagamentoCompleto)
            {
                _boxMancano.BackgroundColor = Ui.C("#43A047");
                if (Resto > 0)
                {
                    _mancano.Text = $"Resto € {Resto:N2}";
                    _resto.Text = $"€ {Resto:N2}";
                    _boxResto.IsVisible = true;
                }
                else
                {
                    _mancano.Text = "✅ Pagamento completo";
                    _boxResto.IsVisible = false;
                }
            }
            else
            {
                decimal mancano = Math.Max(0, _totale - _contantiRicevuti - _cartaImporto);
                _mancano.Text = $"Mancano € {mancano:N2}";
                _boxMancano.BackgroundColor = Ui.C("#E91E63");
                _boxResto.IsVisible = false;
            }

            _btnScontrino.Opacity = PagamentoCompleto ? 1.0 : 0.5;

            if (_cartaImporto > 0)
            {
                _riepilogo.Text = $"💳 Carta: € {_cartaImporto:N2}  ─  💵 Contanti: € {ParteContanti:N2}";
                _boxRiepilogo.IsVisible = true;
            }
            else _boxRiepilogo.IsVisible = false;
        }

        public static Task<EsitoPagamento> Mostra(decimal totale) => Dialogo.Mostra(new CalcoloResto(totale));
    }
}
