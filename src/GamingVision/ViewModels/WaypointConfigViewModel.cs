using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GamingVision.Models;

namespace GamingVision.ViewModels;

/// <summary>
/// ViewModel for the waypoint configuration window.
/// </summary>
public partial class WaypointConfigViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private ObservableCollection<string> _availableLabels = [];

    [ObservableProperty]
    private string? _selectedLabel;

    [ObservableProperty]
    private string _mode = "read";

    [ObservableProperty]
    private float _readIntervalSeconds = 2.0f;

    /// <summary>
    /// Minimum interval value for the slider.
    /// </summary>
    public float MinInterval => 0.5f;

    /// <summary>
    /// Maximum interval value for the slider.
    /// </summary>
    public float MaxInterval => 10.0f;

    /// <summary>
    /// Display text for the current interval.
    /// </summary>
    public string IntervalDisplay => $"{ReadIntervalSeconds:F1}s";

    public WaypointConfigViewModel(List<LabelDefinition> allLabels, WaypointSettings? currentSettings)
    {
        // Populate available labels
        AvailableLabels.Add(""); // Empty option for disabled
        foreach (var label in allLabels.OrderBy(l => l.Name))
        {
            AvailableLabels.Add(label.Name);
        }

        // Load current settings
        if (currentSettings != null)
        {
            IsEnabled = currentSettings.Enabled;
            SelectedLabel = currentSettings.Label;
            Mode = currentSettings.Mode;
            ReadIntervalSeconds = currentSettings.ReadIntervalSeconds;

            // If label is set but not in list, add it
            if (!string.IsNullOrEmpty(currentSettings.Label) && !AvailableLabels.Contains(currentSettings.Label))
            {
                AvailableLabels.Add(currentSettings.Label);
            }
        }
    }

    partial void OnReadIntervalSecondsChanged(float value)
    {
        OnPropertyChanged(nameof(IntervalDisplay));
    }

    partial void OnSelectedLabelChanged(string? value)
    {
        // Auto-enable when a label is selected
        if (!string.IsNullOrEmpty(value))
        {
            IsEnabled = true;
        }
    }

    /// <summary>
    /// Gets the configured settings.
    /// </summary>
    public WaypointSettings GetSettings()
    {
        return new WaypointSettings
        {
            Enabled = IsEnabled && !string.IsNullOrEmpty(SelectedLabel),
            Label = SelectedLabel ?? string.Empty,
            Mode = Mode,
            ReadIntervalSeconds = ReadIntervalSeconds
        };
    }
}
