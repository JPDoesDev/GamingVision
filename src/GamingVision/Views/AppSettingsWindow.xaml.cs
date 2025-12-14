using System.Windows;
using GamingVision.Models;
using GamingVision.Utilities;
using GamingVision.ViewModels;

namespace GamingVision.Views;

/// <summary>
/// Application settings window for configuring global settings.
/// </summary>
public partial class AppSettingsWindow : Window
{
    private readonly AppSettingsViewModel _viewModel;

    public AppSettingsWindow(AppConfiguration config, ConfigManager configManager)
    {
        InitializeComponent();

        _viewModel = new AppSettingsViewModel(config, configManager);
        DataContext = _viewModel;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await _viewModel.SaveAndCloseAsync();
        if (result)
        {
            DialogResult = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
