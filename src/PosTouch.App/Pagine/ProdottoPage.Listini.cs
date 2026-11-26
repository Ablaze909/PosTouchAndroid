using PosTouch.App.Dialoghi;

namespace PosTouch.App.Pagine
{
    /// <summary>ProdottoPage — prezzi dei listini 2, 3 e 4 (0 = come il listino 1, come il PC).</summary>
    public partial class ProdottoPage
    {
        private void AggiornaListini()
        {
            TestoListino2.Text = TestoListino(2, _p.Prezzo2Centesimi);
            TestoListino3.Text = TestoListino(3, _p.Prezzo3Centesimi);
            TestoListino4.Text = TestoListino(4, _p.Prezzo4Centesimi);
        }

        private static string TestoListino(int n, int cent) => cent > 0 ? $"L{n}: € {cent / 100m:N2}" : $"L{n}: = listino 1";

        private async void OnListino2(object sender, TappedEventArgs e) { _p.Prezzo2Centesimi = await ChiediListinoAsync(2, _p.Prezzo2Centesimi); AggiornaListini(); }
        private async void OnListino3(object sender, TappedEventArgs e) { _p.Prezzo3Centesimi = await ChiediListinoAsync(3, _p.Prezzo3Centesimi); AggiornaListini(); }
        private async void OnListino4(object sender, TappedEventArgs e) { _p.Prezzo4Centesimi = await ChiediListinoAsync(4, _p.Prezzo4Centesimi); AggiornaListini(); }

        /// <summary>ANNULLA = invariato; 0 = torna uguale al listino 1.</summary>
        private async Task<int> ChiediListinoAsync(int n, int attuale)
        {
            var v = await TastierinoPrezzo.ChiediEuro($"💶 PREZZO LISTINO {n}\n(0 = come il listino 1)", attuale > 0 ? attuale / 100m : null);
            return v.HasValue && v.Value >= 0 ? (int)Math.Round(v.Value * 100, MidpointRounding.AwayFromZero) : attuale;
        }
    }
}
