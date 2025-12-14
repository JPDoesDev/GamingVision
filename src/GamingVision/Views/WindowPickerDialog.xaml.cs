using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using GamingVision.ViewModels;

namespace GamingVision.Views;

/// <summary>
/// Dialog for selecting a window from the list of running windows.
/// </summary>
public partial class WindowPickerDialog : Window
{
    public ObservableCollection<WindowInfo> Windows { get; } = [];
    public WindowInfo? SelectedWindow { get; private set; }

    private readonly Func<List<WindowInfo>>? _refreshCallback;

    public WindowPickerDialog(List<WindowInfo> windows, Func<List<WindowInfo>>? refreshCallback = null)
    {
        InitializeComponent();
        DataContext = this;

        _refreshCallback = refreshCallback;

        foreach (var window in windows)
        {
            Windows.Add(window);
        }

        if (Windows.Count > 0)
        {
            WindowListBox.SelectedIndex = 0;
        }
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedWindow = WindowListBox.SelectedItem as WindowInfo;
        if (SelectedWindow != null)
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

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_refreshCallback != null)
        {
            var newWindows = _refreshCallback();
            Windows.Clear();
            foreach (var window in newWindows)
            {
                Windows.Add(window);
            }

            if (Windows.Count > 0)
            {
                WindowListBox.SelectedIndex = 0;
            }
        }
    }

    private void WindowListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectedWindow = WindowListBox.SelectedItem as WindowInfo;
        if (SelectedWindow != null)
        {
            DialogResult = true;
            Close();
        }
    }
}
