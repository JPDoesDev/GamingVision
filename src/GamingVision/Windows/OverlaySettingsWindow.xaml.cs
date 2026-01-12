using System.Windows;

namespace GamingVision.Windows;

/// <summary>
/// Window for managing overlay groups.
/// </summary>
public partial class OverlaySettingsWindow : Window
{
    public OverlaySettingsWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
