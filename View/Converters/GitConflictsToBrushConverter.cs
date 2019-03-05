using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GitTfsShell.Data;

namespace GitTfsShell.View.Converters
{
    [ValueConversion(typeof(DialogType), typeof(string))]
    internal sealed class GitConflictsToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is int intValue))
            {
                return Brushes.White;
            }

            return intValue > 0 ? Brushes.OrangeRed : Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}