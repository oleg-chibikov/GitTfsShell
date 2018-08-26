using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Scar.Common.Messages;

namespace GitTfsShell.View.Converters
{
    [ValueConversion(typeof(MessageType), typeof(Color))]
    internal sealed class MessageTypeToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is MessageType))
            {
                return Brushes.Black;
            }

            switch ((MessageType)value)
            {
                case MessageType.Success:
                    return Brushes.ForestGreen;
                case MessageType.Message:
                    return Brushes.Black;
                case MessageType.Warning:
                    return Brushes.Orange;
                case MessageType.Error:
                    return Brushes.OrangeRed;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}