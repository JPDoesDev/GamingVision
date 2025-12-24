using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingVision.Models;

namespace GamingVision.ViewModels;

/// <summary>
/// Represents a label item in the configuration list.
/// </summary>
public partial class LabelConfigItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;
}

/// <summary>
/// ViewModel for the label configuration window.
/// Manages label selection, ordering, and tier-specific settings.
/// </summary>
public partial class LabelConfigurationViewModel : ObservableObject
{
    private readonly List<LabelDefinition> _allLabels;
    private readonly string _tierName;

    [ObservableProperty]
    private ObservableCollection<LabelConfigItem> _selectedLabels = [];

    [ObservableProperty]
    private ObservableCollection<LabelDefinition> _availableLabelsToAdd = [];

    [ObservableProperty]
    private LabelConfigItem? _selectedItem;

    [ObservableProperty]
    private LabelDefinition? _selectedAvailableLabel;

    [ObservableProperty]
    private bool _readLabelAloud;

    [ObservableProperty]
    private bool _autoReadEnabled;

    [ObservableProperty]
    private bool _showAutoReadOption;

    [ObservableProperty]
    private string _windowTitle = "Configure Labels";

    public LabelConfigurationViewModel(
        string tierName,
        List<LabelDefinition> allLabels,
        List<string> currentlySelectedLabels,
        bool readLabelAloud,
        bool? autoReadEnabled = null)
    {
        _tierName = tierName;
        _allLabels = allLabels;
        ReadLabelAloud = readLabelAloud;
        ShowAutoReadOption = autoReadEnabled.HasValue;
        AutoReadEnabled = autoReadEnabled ?? false;
        WindowTitle = $"Configure {tierName} Detection Labels";

        // Load currently selected labels in their priority order
        foreach (var labelName in currentlySelectedLabels)
        {
            var labelDef = allLabels.FirstOrDefault(l => l.Name == labelName);
            if (labelDef != null)
            {
                SelectedLabels.Add(new LabelConfigItem
                {
                    IsSelected = true,
                    Name = labelDef.Name,
                    Description = labelDef.Description
                });
            }
        }

        // Update available labels list
        RefreshAvailableLabels();
    }

    private void RefreshAvailableLabels()
    {
        AvailableLabelsToAdd.Clear();
        var selectedNames = SelectedLabels.Select(l => l.Name).ToHashSet();

        foreach (var label in _allLabels)
        {
            if (!selectedNames.Contains(label.Name))
            {
                AvailableLabelsToAdd.Add(label);
            }
        }
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedItem == null) return;

        var index = SelectedLabels.IndexOf(SelectedItem);
        if (index > 0)
        {
            SelectedLabels.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedItem == null) return;

        var index = SelectedLabels.IndexOf(SelectedItem);
        if (index >= 0 && index < SelectedLabels.Count - 1)
        {
            SelectedLabels.Move(index, index + 1);
        }
    }

    [RelayCommand]
    private void AddLabel()
    {
        if (SelectedAvailableLabel == null) return;

        SelectedLabels.Add(new LabelConfigItem
        {
            IsSelected = true,
            Name = SelectedAvailableLabel.Name,
            Description = SelectedAvailableLabel.Description
        });

        RefreshAvailableLabels();
        SelectedAvailableLabel = null;
    }

    [RelayCommand]
    private void RemoveLabel()
    {
        if (SelectedItem == null) return;

        SelectedLabels.Remove(SelectedItem);
        RefreshAvailableLabels();
        SelectedItem = null;
    }

    /// <summary>
    /// Gets the ordered list of selected label names.
    /// </summary>
    public List<string> GetSelectedLabelNames()
    {
        return SelectedLabels.Select(l => l.Name).ToList();
    }
}
