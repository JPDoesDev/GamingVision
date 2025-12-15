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

    private void PickWindowButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WindowPickerDialog(
            _viewModel.GetAvailableWindows(),
            () => _viewModel.GetAvailableWindows())
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.SelectedWindow != null)
        {
            _viewModel.SetWindowTitle(dialog.SelectedWindow.Title);
        }
    }

    private void ViewLabelsButton_Click(object sender, RoutedEventArgs e)
    {
        var labels = _viewModel.GetAvailableLabels();

        if (labels.Count == 0)
        {
            MessageBox.Show(
                "No labels available. Labels are defined in the game's labelPriority list in game_config.json.",
                "Available Labels",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new AvailableLabelsDialog(labels)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }
}
