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
    }
}
