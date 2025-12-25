using System.Windows;
using GamingVision.Overlay.ViewModels;

namespace GamingVision.Overlay;

/// <summary>
/// Main configuration window for GamingVision Overlay.
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(this);
        DataContext = _viewModel;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Ensure overlay is stopped and resources cleaned up
        _viewModel?.Cleanup();
        Application.Current.Shutdown();
    }
}
