using System.Windows;
using GamingVision.Models;
using GamingVision.Utilities;
using GamingVision.ViewModels;

namespace GamingVision.Views;

/// <summary>
/// Game settings window for configuring per-game settings.
/// </summary>
public partial class GameSettingsWindow : Window
{
    private readonly GameSettingsViewModel _viewModel;

    public GameSettingsWindow(AppConfiguration config, ConfigManager configManager)
    {
        InitializeComponent();

        _viewModel = new GameSettingsViewModel(config, configManager);
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

    private void ConfigurePrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenLabelConfiguration("Primary", this);
    }

    private void ConfigureSecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenLabelConfiguration("Secondary", this);
    }

    private void ConfigureTertiaryButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenLabelConfiguration("Tertiary", this);
    }
}
