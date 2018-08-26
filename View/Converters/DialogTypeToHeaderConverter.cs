using System;
using System.Globalization;
using System.Windows.Data;
using GitTfsShell.Data;

namespace GitTfsShell.View.Converters
{
    [ValueConversion(typeof(DialogType), typeof(string))]
    internal sealed class DialogTypeToHeaderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is DialogType))
            {
                return null;
            }

            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}