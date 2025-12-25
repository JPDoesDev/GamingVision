using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamingVision.Models;

namespace GamingVision.Rendering;

/// <summary>
/// High-performance overlay renderer using DrawingVisual for immediate-mode rendering.
/// Avoids WPF layout system overhead by drawing directly to a visual.
/// </summary>
public class OverlayRenderer
{
    private readonly Canvas _canvas;
    private readonly DrawingVisual _drawingVisual;
    private readonly VisualHost _visualHost;
    private readonly Dictionary<string, SolidColorBrush> _brushCache = new();
    private readonly Dictionary<string, Pen> _penCache = new();
    private readonly Typeface _labelTypeface = new("Segoe UI");

    public OverlayRenderer(Canvas canvas)
    {
        _canvas = canvas;
        _drawingVisual = new DrawingVisual();
        _visualHost = new VisualHost(_drawingVisual);
        _canvas.Children.Add(_visualHost);
    }

    /// <summary>
    /// Clears all drawn elements.
    /// </summary>
    public void Clear()
    {
        using var dc = _drawingVisual.RenderOpen();
        // Just close to clear - nothing to draw
    }

    /// <summary>
    /// Begins a new drawing batch. Call this before drawing multiple boxes.
    /// </summary>
    public DrawingContext BeginDraw()
    {
        return _drawingVisual.RenderOpen();
    }

    /// <summary>
    /// Draws all detections in a single batch for maximum performance.
    /// </summary>
    public void DrawAll(IEnumerable<(DetectedObject detection, OverlayGroup group)> items)
    {
        using var dc = _drawingVisual.RenderOpen();

        foreach (var (det, group) in items)
        {
            DrawBoxInternal(dc, det.X1, det.Y1, det.Width, det.Height, det.Label, group);
        }
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
        using var dc = _drawingVisual.RenderOpen();
        DrawBoxInternal(dc, x, y, width, height, label, group);
    }

    /// <summary>
    /// Internal drawing method that uses an existing DrawingContext.
    /// </summary>
    private void DrawBoxInternal(DrawingContext dc, double x, double y, double width, double height, string label, OverlayGroup group)
    {
        var brush = GetOrCreateBrush(group.Color);
        var pen = GetOrCreatePen(group.Color, group.Thickness);
        var rect = new Rect(x, y, width, height);

        switch (group.Style.ToLowerInvariant())
        {
            case "highcontrastblack":
                DrawHighContrastBox(dc, rect, brush, pen, group.Thickness, true);
                break;
            case "highcontrastwhite":
                DrawHighContrastBox(dc, rect, brush, pen, group.Thickness, false);
                break;
            default: // "outlined"
                dc.DrawRectangle(null, pen, rect);
                break;
        }

        if (group.ShowLabel && !string.IsNullOrEmpty(label))
        {
            DrawLabel(dc, x, y, label, brush, group.Style);
        }
    }

    /// <summary>
    /// Draws a high contrast box with solid fill.
    /// </summary>
    private void DrawHighContrastBox(DrawingContext dc, Rect rect, SolidColorBrush fillBrush, Pen pen, int thickness, bool blackBorder)
    {
        var borderPen = blackBorder
            ? GetOrCreatePen("#000000", thickness)
            : GetOrCreatePen("#FFFFFF", thickness);

        // Draw filled rectangle with border
        dc.DrawRectangle(fillBrush, borderPen, rect);
    }

    /// <summary>
    /// Draws the label text above the bounding box.
    /// </summary>
    private void DrawLabel(DrawingContext dc, double x, double y, string label, SolidColorBrush color, string style)
    {
        var styleLower = style.ToLowerInvariant();
        bool isBlackBorder = styleLower == "highcontrastblack";
        bool isWhiteBorder = styleLower == "highcontrastwhite";

        // Background color
        var bgColor = isBlackBorder ? Colors.Black
                    : isWhiteBorder ? Colors.White
                    : color.Color;
        bgColor.A = 200;

        // Text color
        var textColor = isBlackBorder ? Colors.White
                      : isWhiteBorder ? Colors.Black
                      : Colors.White;

        // Create formatted text
        var formattedText = new FormattedText(
            label,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _labelTypeface,
            14,
            new SolidColorBrush(textColor),
            VisualTreeHelper.GetDpi(_drawingVisual).PixelsPerDip);

        var textWidth = formattedText.Width + 8;  // Add padding
        var textHeight = formattedText.Height + 4;

        var labelY = y - textHeight - 2;
        if (labelY < 0) labelY = y + 2; // If above screen, show inside

        // Draw background rectangle
        var bgRect = new Rect(x, labelY, textWidth, textHeight);
        dc.DrawRoundedRectangle(
            new SolidColorBrush(bgColor),
            null,
            bgRect,
            3, 3);

        // Draw text
        dc.DrawText(formattedText, new Point(x + 4, labelY + 2));
    }

    /// <summary>
    /// Gets or creates a cached brush for the specified color.
    /// </summary>
    private SolidColorBrush GetOrCreateBrush(string hex)
    {
        if (!_brushCache.TryGetValue(hex, out var brush))
        {
            brush = new SolidColorBrush(ParseColor(hex));
            brush.Freeze(); // Freeze for performance
            _brushCache[hex] = brush;
        }
        return brush;
    }

    /// <summary>
    /// Gets or creates a cached pen for the specified color and thickness.
    /// </summary>
    private Pen GetOrCreatePen(string hex, int thickness)
    {
        var key = $"{hex}_{thickness}";
        if (!_penCache.TryGetValue(key, out var pen))
        {
            pen = new Pen(GetOrCreateBrush(hex), thickness);
            pen.Freeze(); // Freeze for performance
            _penCache[key] = pen;
        }
        return pen;
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

/// <summary>
/// Helper class to host a DrawingVisual in a WPF visual tree.
/// Required because DrawingVisual cannot be added directly to Canvas.Children.
/// </summary>
public class VisualHost : FrameworkElement
{
    private readonly VisualCollection _children;

    public VisualHost(DrawingVisual visual)
    {
        _children = new VisualCollection(this) { visual };
    }

    protected override int VisualChildrenCount => _children.Count;

    protected override Visual GetVisualChild(int index) => _children[index];
}
