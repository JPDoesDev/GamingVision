using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GamingVision.Models;

namespace GamingVision.Rendering;

/// <summary>
/// Renders crosshair shapes on a WPF Canvas.
/// Supports Cross, Circle, and Box shapes with configurable colors and thickness.
/// </summary>
public class CrosshairRenderer
{
    private readonly Canvas _canvas;
    private CrosshairSettings? _settings;
    private double _dpiScale = 1.0;

    public CrosshairRenderer(Canvas canvas)
    {
        _canvas = canvas;
    }

    /// <summary>
    /// DPI scale factor for coordinate conversion.
    /// </summary>
    public double DpiScale
    {
        get => _dpiScale;
        set => _dpiScale = value > 0 ? value : 1.0;
    }

    /// <summary>
    /// Updates the crosshair settings and redraws.
    /// </summary>
    public void UpdateSettings(CrosshairSettings settings)
    {
        _settings = settings;
        Draw();
    }

    /// <summary>
    /// Draws the crosshair based on current settings.
    /// </summary>
    public void Draw()
    {
        _canvas.Children.Clear();

        if (_settings == null)
            return;

        // Calculate center position in DIPs (accounting for DPI)
        double centerX = (_canvas.ActualWidth / 2) + (_settings.OffsetX / _dpiScale);
        double centerY = (_canvas.ActualHeight / 2) + (_settings.OffsetY / _dpiScale);

        // Parse colors
        var fillBrush = ParseColor(_settings.Color);
        var outlineBrush = ParseColor(_settings.OutlineColor);

        // Convert pixel sizes to DIPs
        double size = _settings.Size / _dpiScale;
        double thickness = _settings.Thickness / _dpiScale;
        double outlineThickness = _settings.OutlineThickness / _dpiScale;

        switch (_settings.Shape.ToLowerInvariant())
        {
            case "cross":
                DrawCross(centerX, centerY, size, thickness, outlineThickness, fillBrush, outlineBrush);
                break;
            case "circle":
                DrawCircle(centerX, centerY, size, thickness, outlineThickness, fillBrush, outlineBrush);
                break;
            case "box":
                DrawBox(centerX, centerY, size, thickness, outlineThickness, fillBrush, outlineBrush);
                break;
        }
    }

    /// <summary>
    /// Clears the crosshair from the canvas.
    /// </summary>
    public void Clear()
    {
        _canvas.Children.Clear();
    }

    private void DrawCross(double centerX, double centerY, double size, double thickness,
        double outlineThickness, Brush fillBrush, Brush outlineBrush)
    {
        double halfSize = size / 2;
        double totalThickness = thickness + (outlineThickness * 2);

        // Draw outline (if outline thickness > 0)
        if (outlineThickness > 0)
        {
            // Horizontal outline
            var hOutline = new Rectangle
            {
                Width = size,
                Height = totalThickness,
                Fill = outlineBrush
            };
            Canvas.SetLeft(hOutline, centerX - halfSize);
            Canvas.SetTop(hOutline, centerY - (totalThickness / 2));
            _canvas.Children.Add(hOutline);

            // Vertical outline
            var vOutline = new Rectangle
            {
                Width = totalThickness,
                Height = size,
                Fill = outlineBrush
            };
            Canvas.SetLeft(vOutline, centerX - (totalThickness / 2));
            Canvas.SetTop(vOutline, centerY - halfSize);
            _canvas.Children.Add(vOutline);
        }

        // Horizontal line (main color)
        var hLine = new Rectangle
        {
            Width = size,
            Height = thickness,
            Fill = fillBrush
        };
        Canvas.SetLeft(hLine, centerX - halfSize);
        Canvas.SetTop(hLine, centerY - (thickness / 2));
        _canvas.Children.Add(hLine);

        // Vertical line (main color)
        var vLine = new Rectangle
        {
            Width = thickness,
            Height = size,
            Fill = fillBrush
        };
        Canvas.SetLeft(vLine, centerX - (thickness / 2));
        Canvas.SetTop(vLine, centerY - halfSize);
        _canvas.Children.Add(vLine);
    }

    private void DrawCircle(double centerX, double centerY, double size, double thickness,
        double outlineThickness, Brush fillBrush, Brush outlineBrush)
    {
        double diameter = size;
        double totalStroke = thickness + (outlineThickness * 2);

        // Draw outline circle (if outline thickness > 0)
        if (outlineThickness > 0)
        {
            var outline = new Ellipse
            {
                Width = diameter + totalStroke,
                Height = diameter + totalStroke,
                Stroke = outlineBrush,
                StrokeThickness = outlineThickness,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(outline, centerX - (diameter + totalStroke) / 2);
            Canvas.SetTop(outline, centerY - (diameter + totalStroke) / 2);
            _canvas.Children.Add(outline);

            // Inner outline
            var innerOutline = new Ellipse
            {
                Width = diameter - thickness,
                Height = diameter - thickness,
                Stroke = outlineBrush,
                StrokeThickness = outlineThickness,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(innerOutline, centerX - (diameter - thickness) / 2);
            Canvas.SetTop(innerOutline, centerY - (diameter - thickness) / 2);
            _canvas.Children.Add(innerOutline);
        }

        // Main circle
        var circle = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Stroke = fillBrush,
            StrokeThickness = thickness,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(circle, centerX - (diameter / 2));
        Canvas.SetTop(circle, centerY - (diameter / 2));
        _canvas.Children.Add(circle);
    }

    private void DrawBox(double centerX, double centerY, double size, double thickness,
        double outlineThickness, Brush fillBrush, Brush outlineBrush)
    {
        double halfSize = size / 2;
        double totalStroke = thickness + (outlineThickness * 2);

        // Draw outline box (if outline thickness > 0)
        if (outlineThickness > 0)
        {
            var outline = new Rectangle
            {
                Width = size + totalStroke,
                Height = size + totalStroke,
                Stroke = outlineBrush,
                StrokeThickness = outlineThickness,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(outline, centerX - (size + totalStroke) / 2);
            Canvas.SetTop(outline, centerY - (size + totalStroke) / 2);
            _canvas.Children.Add(outline);

            // Inner outline
            var innerOutline = new Rectangle
            {
                Width = size - thickness,
                Height = size - thickness,
                Stroke = outlineBrush,
                StrokeThickness = outlineThickness,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(innerOutline, centerX - (size - thickness) / 2);
            Canvas.SetTop(innerOutline, centerY - (size - thickness) / 2);
            _canvas.Children.Add(innerOutline);
        }

        // Main box
        var box = new Rectangle
        {
            Width = size,
            Height = size,
            Stroke = fillBrush,
            StrokeThickness = thickness,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(box, centerX - halfSize);
        Canvas.SetTop(box, centerY - halfSize);
        _canvas.Children.Add(box);
    }

    private static SolidColorBrush ParseColor(string hexColor)
    {
        try
        {
            if (string.IsNullOrEmpty(hexColor))
                return Brushes.White;

            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            return new SolidColorBrush(color);
        }
        catch
        {
            return Brushes.White;
        }
    }
}
