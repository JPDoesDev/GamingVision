using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GamingVision.Models;

namespace GamingVision.Overlay;

/// <summary>
/// Dialog for editing overlay group properties.
/// </summary>
public partial class GroupEditorWindow : Window, INotifyPropertyChanged
{
    private readonly OverlayGroup _group;
    private readonly List<string> _allLabels;

    public event PropertyChangedEventHandler? PropertyChanged;

    public GroupEditorWindow(OverlayGroup group, List<string> availableLabels)
    {
        InitializeComponent();
        DataContext = this;

        _group = group;
        _allLabels = availableLabels;

        // Initialize properties from group
        GroupName = group.Name;
        Color = group.Color;
        Thickness = group.Thickness;
        ShowLabel = group.ShowLabel;
        ConfidenceThreshold = group.ConfidenceThreshold;
        SetStyleFromString(group.Style);

        // Initialize label lists
        SelectedLabels = new ObservableCollection<string>(group.Labels);
        AvailableLabels = new ObservableCollection<string>(
            _allLabels.Where(l => !group.Labels.Contains(l)));

        // Subscribe to property changes for preview
        PropertyChanged += (s, e) => UpdatePreview();
        Loaded += (s, e) => UpdatePreview();
    }

    public OverlayGroup Group => _group;

    // Bindable properties
    private string _groupName = string.Empty;
    public string GroupName
    {
        get => _groupName;
        set { _groupName = value; OnPropertyChanged(); }
    }

    private string _color = "#FF0000";
    private bool _updatingColor;

    public string Color
    {
        get => _color;
        set
        {
            if (_color != value)
            {
                _color = value;
                OnPropertyChanged();

                // Update RGB sliders from hex (avoid circular updates)
                if (!_updatingColor)
                {
                    _updatingColor = true;
                    try
                    {
                        var color = ParseColor(value);
                        _red = color.R;
                        _green = color.G;
                        _blue = color.B;
                        OnPropertyChanged(nameof(Red));
                        OnPropertyChanged(nameof(Green));
                        OnPropertyChanged(nameof(Blue));
                    }
                    finally
                    {
                        _updatingColor = false;
                    }
                }
            }
        }
    }

    private int _red = 255;
    public int Red
    {
        get => _red;
        set
        {
            if (_red != value)
            {
                _red = Math.Clamp(value, 0, 255);
                OnPropertyChanged();
                UpdateColorFromRgb();
            }
        }
    }

    private int _green;
    public int Green
    {
        get => _green;
        set
        {
            if (_green != value)
            {
                _green = Math.Clamp(value, 0, 255);
                OnPropertyChanged();
                UpdateColorFromRgb();
            }
        }
    }

    private int _blue;
    public int Blue
    {
        get => _blue;
        set
        {
            if (_blue != value)
            {
                _blue = Math.Clamp(value, 0, 255);
                OnPropertyChanged();
                UpdateColorFromRgb();
            }
        }
    }

    private void UpdateColorFromRgb()
    {
        if (!_updatingColor)
        {
            _updatingColor = true;
            try
            {
                Color = $"#{_red:X2}{_green:X2}{_blue:X2}";
            }
            finally
            {
                _updatingColor = false;
            }
        }
    }

    private int _thickness = 2;
    public int Thickness
    {
        get => _thickness;
        set { _thickness = value; OnPropertyChanged(); }
    }

    private bool _showLabel = true;
    public bool ShowLabel
    {
        get => _showLabel;
        set { _showLabel = value; OnPropertyChanged(); }
    }

    private float _confidenceThreshold = 0.1f;
    public float ConfidenceThreshold
    {
        get => _confidenceThreshold;
        set { _confidenceThreshold = value; OnPropertyChanged(); }
    }

    // Style dropdown
    public List<StyleOption> StyleOptions { get; } = new()
    {
        new StyleOption("outlined", "Outlined"),
        new StyleOption("highContrastBlack", "High Contrast (Black border)"),
        new StyleOption("highContrastWhite", "High Contrast (White border)")
    };

    private StyleOption? _selectedStyle;
    public StyleOption? SelectedStyle
    {
        get => _selectedStyle;
        set
        {
            _selectedStyle = value;
            OnPropertyChanged();
            UpdatePreview();
        }
    }

    public ObservableCollection<string> AvailableLabels { get; }
    public ObservableCollection<string> SelectedLabels { get; }

    private void SetStyleFromString(string style)
    {
        SelectedStyle = StyleOptions.FirstOrDefault(s =>
            s.Value.Equals(style, StringComparison.OrdinalIgnoreCase)) ?? StyleOptions[0];
    }

    private string GetStyleString()
    {
        return SelectedStyle?.Value ?? "solid";
    }

    private void UpdatePreview()
    {
        PreviewCanvas.Children.Clear();

        try
        {
            var color = ParseColor(Color);
            var brush = new SolidColorBrush(color);

            // Center the preview in the canvas
            double width = 80, height = 25;
            double x = (PreviewCanvas.ActualWidth - width) / 2;
            double y = (PreviewCanvas.ActualHeight - height) / 2;
            if (x < 10) x = 10;
            if (y < 5) y = 5;

            switch (GetStyleString())
            {
                case "highContrastBlack":
                    DrawHighContrastPreview(x, y, width, height, brush, true);
                    break;
                case "highContrastWhite":
                    DrawHighContrastPreview(x, y, width, height, brush, false);
                    break;
                default: // "outlined"
                    DrawOutlinedPreview(x, y, width, height, brush);
                    break;
            }

            if (ShowLabel)
            {
                DrawLabelPreview(x, y, "Label");
            }
        }
        catch
        {
            // Ignore preview errors
        }
    }

    private void DrawOutlinedPreview(double x, double y, double width, double height, SolidColorBrush brush)
    {
        var rect = new Rectangle
        {
            Width = width, Height = height,
            Stroke = brush, StrokeThickness = Thickness,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        PreviewCanvas.Children.Add(rect);
    }

    private void DrawHighContrastPreview(double x, double y, double width, double height, SolidColorBrush brush, bool blackBorder)
    {
        var borderBrush = blackBorder ? Brushes.Black : Brushes.White;

        var rect = new Rectangle
        {
            Width = width, Height = height,
            Stroke = borderBrush,
            StrokeThickness = Thickness,
            Fill = brush
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        PreviewCanvas.Children.Add(rect);
    }

    private void DrawLabelPreview(double x, double y, string label)
    {
        var color = ParseColor(Color);
        color.A = 200;

        var textBlock = new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.Bold
        };

        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var bgRect = new Rectangle
        {
            Width = textBlock.DesiredSize.Width + 8,
            Height = textBlock.DesiredSize.Height + 4,
            Fill = new SolidColorBrush(color),
            RadiusX = 3, RadiusY = 3
        };

        Canvas.SetLeft(bgRect, x);
        Canvas.SetTop(bgRect, y - bgRect.Height - 2);
        PreviewCanvas.Children.Add(bgRect);

        Canvas.SetLeft(textBlock, x + 4);
        Canvas.SetTop(textBlock, y - textBlock.DesiredSize.Height - 4);
        PreviewCanvas.Children.Add(textBlock);
    }

    private static System.Windows.Media.Color ParseColor(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
                return System.Windows.Media.Color.FromRgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
        }
        catch { }
        return Colors.Red;
    }

    private void AddLabel_Click(object sender, RoutedEventArgs e)
    {
        var selected = AvailableLabelsListBox.SelectedItems.Cast<string>().ToList();
        foreach (var label in selected)
        {
            AvailableLabels.Remove(label);
            SelectedLabels.Add(label);
        }
    }

    private void RemoveLabel_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedLabelsListBox.SelectedItems.Cast<string>().ToList();
        foreach (var label in selected)
        {
            SelectedLabels.Remove(label);
            AvailableLabels.Add(label);
        }
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GroupName))
        {
            MessageBox.Show("Please enter a group name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Update the group
        _group.Name = GroupName;
        _group.Color = Color;
        _group.Thickness = Thickness;
        _group.ShowLabel = ShowLabel;
        _group.ConfidenceThreshold = ConfidenceThreshold;
        _group.Style = GetStyleString();
        _group.Labels = SelectedLabels.ToList();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Represents a box style option for the dropdown.
/// </summary>
public class StyleOption
{
    public string Value { get; }
    public string DisplayName { get; }

    public StyleOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public override string ToString() => DisplayName;
}
