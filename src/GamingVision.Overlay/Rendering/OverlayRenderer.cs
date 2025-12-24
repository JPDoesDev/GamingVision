using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using GamingVision.Models;

namespace GamingVision.Overlay.Rendering;

/// <summary>
/// Renders bounding boxes on a WPF Canvas with various visual styles.
/// </summary>
public class OverlayRenderer
{
    private readonly Canvas _canvas;

    public OverlayRenderer(Canvas canvas)
    {
        _canvas = canvas;
    }

    /// <summary>
    /// Clears all drawn elements from the canvas.
    /// </summary>
    public void Clear()
    {
        _canvas.Children.Clear();
    }

    /// <summary>
    /// Draws a bounding box for a detected object.
    /// </summary>
    /// <param name="x">Left position in screen coordinates</param>
    /// <param name="y">Top position in screen coordinates</param>
    /// <param name="width">Width of bounding box</param>
    /// <param name="height">Height of bounding box</param>
    /// <param name="label">Label text to display</param>
    /// <param name="group">Overlay group defining visual style</param>
    public void DrawBox(double x, double y, double width, double height, string label, OverlayGroup group)
    {
        var color = ParseColor(group.Color);
        var brush = new SolidColorBrush(color);

        switch (group.Style.ToLowerInvariant())
        {
            case "dashed":
                DrawDashedBox(x, y, width, height, brush, group.Thickness);
                break;
            case "filled":
                DrawFilledBox(x, y, width, height, brush, group.Thickness);
                break;
            case "highcontrast":
                DrawHighContrastBox(x, y, width, height, brush, group.Thickness, false);
                break;
            case "highcontrastinverted":
                DrawHighContrastBox(x, y, width, height, brush, group.Thickness, true);
                break;
            default: // "solid"
                DrawSolidBox(x, y, width, height, brush, group.Thickness);
                break;
        }

        if (group.ShowLabel && !string.IsNullOrEmpty(label))
        {
            DrawLabel(x, y, label, brush, group.Style);
        }
    }

    /// <summary>
    /// Draws a solid outline box.
    /// </summary>
    private void DrawSolidBox(double x, double y, double width, double height, SolidColorBrush stroke, int thickness)
    {
        var rect = new Rectangle
        {
            Width = width,
            Height = height,
            Stroke = stroke,
            StrokeThickness = thickness,
            Fill = Brushes.Transparent
        };

        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        _canvas.Children.Add(rect);
    }

    /// <summary>
    /// Draws a dashed outline box.
    /// </summary>
    private void DrawDashedBox(double x, double y, double width, double height, SolidColorBrush stroke, int thickness)
    {
        var rect = new Rectangle
        {
            Width = width,
            Height = height,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Fill = Brushes.Transparent
        };

        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        _canvas.Children.Add(rect);
    }

    /// <summary>
    /// Draws a box with semi-transparent fill.
    /// </summary>
    private void DrawFilledBox(double x, double y, double width, double height, SolidColorBrush stroke, int thickness)
    {
        var fillColor = stroke.Color;
        fillColor.A = 64; // 25% opacity

        var rect = new Rectangle
        {
            Width = width,
            Height = height,
            Stroke = stroke,
            StrokeThickness = thickness,
            Fill = new SolidColorBrush(fillColor)
        };

        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        _canvas.Children.Add(rect);
    }

    /// <summary>
    /// Draws a high contrast box with thick border and colored fill.
    /// </summary>
    private void DrawHighContrastBox(double x, double y, double width, double height, SolidColorBrush colorBrush, int thickness, bool inverted)
    {
        var borderColor = inverted ? Colors.Black : Colors.White;
        var fillColor = colorBrush.Color;
        fillColor.A = 128; // 50% opacity

        // Outer border (white or black)
        var outerRect = new Rectangle
        {
            Width = width + (thickness * 2),
            Height = height + (thickness * 2),
            Stroke = new SolidColorBrush(borderColor),
            StrokeThickness = thickness,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(outerRect, x - thickness);
        Canvas.SetTop(outerRect, y - thickness);
        _canvas.Children.Add(outerRect);

        // Inner colored fill
        var innerRect = new Rectangle
        {
            Width = width,
            Height = height,
            Stroke = colorBrush,
            StrokeThickness = thickness,
            Fill = new SolidColorBrush(fillColor)
        };
        Canvas.SetLeft(innerRect, x);
        Canvas.SetTop(innerRect, y);
        _canvas.Children.Add(innerRect);
    }

    /// <summary>
    /// Draws the label text above the bounding box.
    /// </summary>
    private void DrawLabel(double x, double y, string label, SolidColorBrush color, string style)
    {
        bool isHighContrast = style.ToLowerInvariant().StartsWith("highcontrast");
        bool inverted = style.ToLowerInvariant().Contains("inverted");

        // Background for label
        var bgColor = isHighContrast
            ? (inverted ? Colors.Black : Colors.White)
            : color.Color;
        bgColor.A = 200;

        var textColor = isHighContrast
            ? (inverted ? Colors.White : Colors.Black)
            : Colors.White;

        var textBlock = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(textColor),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(4, 2, 4, 2)
        };

        // Measure text size
        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = textBlock.DesiredSize.Width;
        var textHeight = textBlock.DesiredSize.Height;

        // Background rectangle
        var bgRect = new Rectangle
        {
            Width = textWidth,
            Height = textHeight,
            Fill = new SolidColorBrush(bgColor),
            RadiusX = 3,
            RadiusY = 3
        };

        var labelY = y - textHeight - 2;
        if (labelY < 0) labelY = y + 2; // If above screen, show inside

        Canvas.SetLeft(bgRect, x);
        Canvas.SetTop(bgRect, labelY);
        _canvas.Children.Add(bgRect);

        Canvas.SetLeft(textBlock, x);
        Canvas.SetTop(textBlock, labelY);
        _canvas.Children.Add(textBlock);
    }

    /// <summary>
    /// Parses a hex color string into a Color.
    /// </summary>
    private static Color ParseColor(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');

            if (hex.Length == 6)
            {
                return Color.FromRgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
            }
            else if (hex.Length == 8)
            {
                return Color.FromArgb(
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16),
                    Convert.ToByte(hex.Substring(6, 2), 16));
            }
        }
        catch
        {
            // Fallback to red if parse fails
        }

        return Colors.Red;
    }
}
