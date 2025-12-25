using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GamingVision.Converters;

/// <summary>
/// Converts a hex color string to a SolidColorBrush.
/// </summary>
public class ColorStringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hexColor)
        {
            try
            {
                hexColor = hexColor.TrimStart('#');

                if (hexColor.Length == 6)
                {
                    var color = Color.FromRgb(
                        System.Convert.ToByte(hexColor.Substring(0, 2), 16),
                        System.Convert.ToByte(hexColor.Substring(2, 2), 16),
                        System.Convert.ToByte(hexColor.Substring(4, 2), 16));
                    return new SolidColorBrush(color);
                }
                else if (hexColor.Length == 8)
                {
                    var color = Color.FromArgb(
                        System.Convert.ToByte(hexColor.Substring(0, 2), 16),
                        System.Convert.ToByte(hexColor.Substring(2, 2), 16),
                        System.Convert.ToByte(hexColor.Substring(4, 2), 16),
                        System.Convert.ToByte(hexColor.Substring(6, 2), 16));
                    return new SolidColorBrush(color);
                }
            }
            catch
            {
                // Fallback
            }
        }

        return new SolidColorBrush(Colors.Red);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
