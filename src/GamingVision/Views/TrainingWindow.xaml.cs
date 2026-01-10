using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GamingVision.Models;
using GamingVision.Utilities;
using GamingVision.ViewModels;

namespace GamingVision.Views;

/// <summary>
/// Window for managing the training workflow.
/// </summary>
public partial class TrainingWindow : Window
{
    private readonly TrainingWindowViewModel _viewModel;

    public TrainingWindow(ConfigManager configManager, GameProfile profile)
    {
        InitializeComponent();

        _viewModel = new TrainingWindowViewModel(configManager, profile);
        DataContext = _viewModel;

        Closing += TrainingWindow_Closing;
    }

    private void TrainingWindow_Closing(object? sender, CancelEventArgs e)
    {
        _viewModel.Dispose();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

/// <summary>
/// Converts a boolean to a color (green for true, red for false).
/// </summary>
public class BoolToStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool passed)
        {
            return passed ? Brushes.LimeGreen : Brushes.OrangeRed;
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts a boolean value.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        return value;
    }
}

/// <summary>
/// Converts a boolean to Visibility (false = Visible, true = Collapsed).
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
