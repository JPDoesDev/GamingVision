using System.Windows;
using GamingVision.Models;
using GamingVision.Utilities;
using GamingVision.ViewModels;

namespace GamingVision.Views;

/// <summary>
/// Window for creating a new game profile.
/// </summary>
public partial class CreateProfileWindow : Window
{
    private readonly CreateProfileViewModel _viewModel;

    /// <summary>
    /// Gets the created game profile, or null if the dialog was cancelled.
    /// </summary>
    public GameProfile? CreatedProfile => _viewModel.CreatedProfile;

    public CreateProfileWindow(ConfigManager configManager)
    {
        InitializeComponent();

        _viewModel = new CreateProfileViewModel(configManager);
        DataContext = _viewModel;

        // Focus the display name field
        Loaded += (s, e) => DisplayNameTextBox.Focus();
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var success = await _viewModel.CreateProfileAsync();
        if (success)
        {
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show(
                _viewModel.ErrorMessage ?? "Failed to create profile.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
