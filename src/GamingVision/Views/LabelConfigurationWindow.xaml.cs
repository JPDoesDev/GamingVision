using System.Windows;
using GamingVision.ViewModels;

namespace GamingVision.Views;

/// <summary>
/// Interaction logic for LabelConfigurationWindow.xaml
/// </summary>
public partial class LabelConfigurationWindow : Window
{
    public LabelConfigurationWindow(LabelConfigurationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>
    /// Gets the ViewModel for accessing results.
    /// </summary>
    public LabelConfigurationViewModel ViewModel => (LabelConfigurationViewModel)DataContext;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
