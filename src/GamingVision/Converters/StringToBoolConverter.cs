using System.Globalization;
using System.Windows.Data;

namespace GamingVision.Converters;

/// <summary>
/// Converts a string to boolean for radio button binding.
/// Returns true if the bound value equals the converter parameter.
/// </summary>
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string stringValue && parameter is string paramValue)
        {
            return stringValue.Equals(paramValue, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && boolValue && parameter is string paramValue)
        {
            return paramValue;
        }
        return Binding.DoNothing;
    }
}
