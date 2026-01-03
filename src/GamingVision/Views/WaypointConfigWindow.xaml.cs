using System.Windows;
using GamingVision.ViewModels;

namespace GamingVision.Views;

/// <summary>
/// Dialog for configuring waypoint tracker settings.
/// </summary>
public partial class WaypointConfigWindow : Window
{
    private readonly WaypointConfigViewModel _viewModel;

    public WaypointConfigWindow(WaypointConfigViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>
    /// Gets the view model for retrieving configured settings.
    /// </summary>
    public WaypointConfigViewModel ViewModel => _viewModel;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
