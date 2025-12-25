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
            case "highcontrastblack":
                DrawHighContrastBox(x, y, width, height, brush, group.Thickness, true);
                break;
            case "highcontrastwhite":
                DrawHighContrastBox(x, y, width, height, brush, group.Thickness, false);
                break;
            default: // "outlined"
                DrawOutlinedBox(x, y, width, height, brush, group.Thickness);
                break;
        }

        if (group.ShowLabel && !string.IsNullOrEmpty(label))
        {
            DrawLabel(x, y, label, brush, group.Style);
        }
    }

    /// <summary>
    /// Draws an outlined box with the specified color.
    /// </summary>
    private void DrawOutlinedBox(double x, double y, double width, double height, SolidColorBrush stroke, int thickness)
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
    /// Draws a high contrast box with border and solid colored fill.
    /// </summary>
    private void DrawHighContrastBox(double x, double y, double width, double height, SolidColorBrush fillBrush, int thickness, bool blackBorder)
    {
        var borderBrush = blackBorder ? Brushes.Black : Brushes.White;

        var rect = new Rectangle
        {
            Width = width,
            Height = height,
            Stroke = borderBrush,
            StrokeThickness = thickness,
            Fill = fillBrush
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        _canvas.Children.Add(rect);
    }

    /// <summary>
    /// Draws the label text above the bounding box.
    /// </summary>
    private void DrawLabel(double x, double y, string label, SolidColorBrush color, string style)
    {
        var styleLower = style.ToLowerInvariant();
        bool isBlackBorder = styleLower == "highcontrastblack";
        bool isWhiteBorder = styleLower == "highcontrastwhite";

        // Background for label
        var bgColor = isBlackBorder ? Colors.Black
                    : isWhiteBorder ? Colors.White
                    : color.Color;
        bgColor.A = 200;

        var textColor = isBlackBorder ? Colors.White
                      : isWhiteBorder ? Colors.Black
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
