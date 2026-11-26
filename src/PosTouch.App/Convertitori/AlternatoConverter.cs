using System.Globalization;

namespace PosTouch.App.Convertitori
{
    /// <summary>Sfondo alternato delle righe scontrino come sul PC: bianco / #EEEEEE.</summary>
    public class AlternatoConverter : IValueConverter
    {
        private static readonly Color Pari = Colors.White;
        private static readonly Color Dispari = Color.FromArgb("#EEEEEE");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Dispari : Pari;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
