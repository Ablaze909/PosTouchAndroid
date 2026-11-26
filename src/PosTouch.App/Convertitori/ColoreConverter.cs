using System.Globalization;

namespace PosTouch.App.Convertitori
{
    /// <summary>Converte il colore salvato nel DB ("#RRGGBB") in Color; se non valido usa il parametro o il grigio.</summary>
    public class ColoreConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s) && Color.TryParse(s.Trim(), out var c))
                return c;
            if (parameter is string p && Color.TryParse(p, out var d))
                return d;
            return Color.FromArgb("#546E7A");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
