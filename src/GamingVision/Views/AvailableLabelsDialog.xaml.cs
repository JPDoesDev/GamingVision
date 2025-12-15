using System.Windows;

namespace GamingVision.Views;

/// <summary>
/// Dialog showing available model labels for the current game profile.
/// </summary>
public partial class AvailableLabelsDialog : Window
{
    public AvailableLabelsDialog(List<string> labels)
    {
        InitializeComponent();
        LabelsListBox.ItemsSource = labels;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
