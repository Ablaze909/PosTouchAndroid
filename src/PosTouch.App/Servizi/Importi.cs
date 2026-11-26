using System.Globalization;

namespace PosTouch.App.Servizi
{
    /// <summary>Conversione degli importi digitati dall'operatore ("3,50" o "3.50") in centesimi.</summary>
    public static class Importi
    {
        public static bool ProvaCentesimi(string testo, out int centesimi)
        {
            centesimi = 0;
            if (string.IsNullOrWhiteSpace(testo)) return false;
            string t = testo.Trim().Replace("€", "").Replace(" ", "").Replace(',', '.');
            if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal euro)) return false;
            if (euro <= 0 || euro > 99999) return false;
            centesimi = (int)Math.Round(euro * 100, MidpointRounding.AwayFromZero);
            return true;
        }

        public static bool ProvaDecimale(string testo, out decimal valore)
        {
            valore = 0;
            if (string.IsNullOrWhiteSpace(testo)) return false;
            return decimal.TryParse(testo.Trim().Replace("%", "").Replace(',', '.'),
                NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out valore) && valore > 0;
        }

        public static string Euro(int centesimi) => $"€ {centesimi / 100m:N2}";
    }
}
