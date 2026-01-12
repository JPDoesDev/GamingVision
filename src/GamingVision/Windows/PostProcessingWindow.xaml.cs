using System.Windows;

namespace GamingVision.Windows;

/// <summary>
/// Window for batch post-processing training images.
/// </summary>
public partial class PostProcessingWindow : Window
{
    public PostProcessingWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
