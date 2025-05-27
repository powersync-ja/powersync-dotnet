using System.Globalization;
using System.Windows.Data;

namespace PowersyncDotnetTodoList.Converters
{
    public class BoolToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool connected)
            {
                return connected ? "Connected" : "Disconnected";
            }
            return "Disconnected"; // Default if the value isn't a boolean
        }

        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            return value is string str && str == "Connected";
        }
    }
}
