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
        Name = group.Name;
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
    private string _name = string.Empty;
    public new string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    private string _color = "#FF0000";
    public string Color
    {
        get => _color;
        set { _color = value; OnPropertyChanged(); }
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

    // Style radio button bindings
    private bool _isSolid = true;
    public bool IsSolid
    {
        get => _isSolid;
        set { if (value) { _isSolid = true; _isDashed = _isFilled = _isHighContrast = _isHighContrastInverted = false; NotifyStyleChanged(); } }
    }

    private bool _isDashed;
    public bool IsDashed
    {
        get => _isDashed;
        set { if (value) { _isDashed = true; _isSolid = _isFilled = _isHighContrast = _isHighContrastInverted = false; NotifyStyleChanged(); } }
    }

    private bool _isFilled;
    public bool IsFilled
    {
        get => _isFilled;
        set { if (value) { _isFilled = true; _isSolid = _isDashed = _isHighContrast = _isHighContrastInverted = false; NotifyStyleChanged(); } }
    }

    private bool _isHighContrast;
    public bool IsHighContrast
    {
        get => _isHighContrast;
        set { if (value) { _isHighContrast = true; _isSolid = _isDashed = _isFilled = _isHighContrastInverted = false; NotifyStyleChanged(); } }
    }

    private bool _isHighContrastInverted;
    public bool IsHighContrastInverted
    {
        get => _isHighContrastInverted;
        set { if (value) { _isHighContrastInverted = true; _isSolid = _isDashed = _isFilled = _isHighContrast = false; NotifyStyleChanged(); } }
    }

    private void NotifyStyleChanged()
    {
        OnPropertyChanged(nameof(IsSolid));
        OnPropertyChanged(nameof(IsDashed));
        OnPropertyChanged(nameof(IsFilled));
        OnPropertyChanged(nameof(IsHighContrast));
        OnPropertyChanged(nameof(IsHighContrastInverted));
    }

    public ObservableCollection<string> AvailableLabels { get; }
    public ObservableCollection<string> SelectedLabels { get; }

    private void SetStyleFromString(string style)
    {
        switch (style.ToLowerInvariant())
        {
            case "dashed":
                IsDashed = true;
                break;
            case "filled":
                IsFilled = true;
                break;
            case "highcontrast":
                IsHighContrast = true;
                break;
            case "highcontrastinverted":
                IsHighContrastInverted = true;
                break;
            default:
                IsSolid = true;
                break;
        }
    }

    private string GetStyleString()
    {
        if (IsDashed) return "dashed";
        if (IsFilled) return "filled";
        if (IsHighContrast) return "highContrast";
        if (IsHighContrastInverted) return "highContrastInverted";
        return "solid";
    }

    private void UpdatePreview()
    {
        PreviewCanvas.Children.Clear();

        try
        {
            var color = ParseColor(Color);
            var brush = new SolidColorBrush(color);

            double x = 20, y = 10, width = 100, height = 40;

            switch (GetStyleString())
            {
                case "dashed":
                    DrawDashedPreview(x, y, width, height, brush);
                    break;
                case "filled":
                    DrawFilledPreview(x, y, width, height, brush);
                    break;
                case "highContrast":
                    DrawHighContrastPreview(x, y, width, height, brush, false);
                    break;
                case "highContrastInverted":
                    DrawHighContrastPreview(x, y, width, height, brush, true);
                    break;
                default:
                    DrawSolidPreview(x, y, width, height, brush);
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

    private void DrawSolidPreview(double x, double y, double width, double height, SolidColorBrush brush)
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

    private void DrawDashedPreview(double x, double y, double width, double height, SolidColorBrush brush)
    {
        var rect = new Rectangle
        {
            Width = width, Height = height,
            Stroke = brush, StrokeThickness = Thickness,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        PreviewCanvas.Children.Add(rect);
    }

    private void DrawFilledPreview(double x, double y, double width, double height, SolidColorBrush brush)
    {
        var fillColor = brush.Color;
        fillColor.A = 64;

        var rect = new Rectangle
        {
            Width = width, Height = height,
            Stroke = brush, StrokeThickness = Thickness,
            Fill = new SolidColorBrush(fillColor)
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        PreviewCanvas.Children.Add(rect);
    }

    private void DrawHighContrastPreview(double x, double y, double width, double height, SolidColorBrush brush, bool inverted)
    {
        var borderColor = inverted ? Colors.Black : Colors.White;
        var fillColor = brush.Color;
        fillColor.A = 128;

        var outerRect = new Rectangle
        {
            Width = width + (Thickness * 2),
            Height = height + (Thickness * 2),
            Stroke = new SolidColorBrush(borderColor),
            StrokeThickness = Thickness,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(outerRect, x - Thickness);
        Canvas.SetTop(outerRect, y - Thickness);
        PreviewCanvas.Children.Add(outerRect);

        var innerRect = new Rectangle
        {
            Width = width, Height = height,
            Stroke = brush, StrokeThickness = Thickness,
            Fill = new SolidColorBrush(fillColor)
        };
        Canvas.SetLeft(innerRect, x);
        Canvas.SetTop(innerRect, y);
        PreviewCanvas.Children.Add(innerRect);
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
        if (string.IsNullOrWhiteSpace(Name))
        {
            MessageBox.Show("Please enter a group name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Update the group
        _group.Name = Name;
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
