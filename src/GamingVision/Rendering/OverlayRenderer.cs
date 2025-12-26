using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamingVision.Models;
using GamingVision.Utilities;

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
    private readonly Dictionary<string, FormattedText> _textCache = new();
    private readonly Typeface _labelTypeface = new("Segoe UI");
    private readonly double _pixelsPerDip;

    /// <summary>
    /// DPI scale factor used to convert physical pixel coordinates to WPF DIPs.
    /// Set this before calling DrawAll to ensure proper coordinate alignment.
    /// Default is 1.0 (100% scaling / 96 DPI).
    /// </summary>
    public double DpiScale { get; set; } = 1.0;

    // Pre-cached brushes for common colors
    private static readonly SolidColorBrush BlackBgBrush;
    private static readonly SolidColorBrush WhiteBgBrush;
    private static readonly SolidColorBrush WhiteTextBrush;
    private static readonly SolidColorBrush BlackTextBrush;

    static OverlayRenderer()
    {
        // Pre-create and freeze common brushes
        var blackBg = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
        blackBg.Freeze();
        BlackBgBrush = blackBg;

        var whiteBg = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        whiteBg.Freeze();
        WhiteBgBrush = whiteBg;

        var whiteText = new SolidColorBrush(Colors.White);
        whiteText.Freeze();
        WhiteTextBrush = whiteText;

        var blackText = new SolidColorBrush(Colors.Black);
        blackText.Freeze();
        BlackTextBrush = blackText;
    }

    public OverlayRenderer(Canvas canvas)
    {
        _canvas = canvas;
        _drawingVisual = new DrawingVisual();
        _visualHost = new VisualHost(_drawingVisual);
        _canvas.Children.Add(_visualHost);

        // Cache DPI value - rarely changes
        _pixelsPerDip = VisualTreeHelper.GetDpi(_drawingVisual).PixelsPerDip;
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
    /// Coordinates are scaled from physical pixels to WPF DIPs using DpiScale.
    /// </summary>
    public void DrawAll(IEnumerable<(DetectedObject detection, OverlayGroup group)> items)
    {
        var sw = Stopwatch.StartNew();
        int count = 0;

        // Calculate inverse DPI scale for converting physical pixels to DIPs
        // At 125% DPI (1.25), we divide coordinates by 1.25 to get correct DIP positions
        double invDpiScale = 1.0 / DpiScale;

        using var dc = _drawingVisual.RenderOpen();

        foreach (var (det, group) in items)
        {
            // Scale detection coordinates from physical pixels to WPF DIPs
            double x = det.X1 * invDpiScale;
            double y = det.Y1 * invDpiScale;
            double width = det.Width * invDpiScale;
            double height = det.Height * invDpiScale;

            DrawBoxInternal(dc, x, y, width, height, det.Label, group);
            count++;
        }

        var renderMs = sw.ElapsedMilliseconds;
        Logger.Log($"[PERF] Render: draw={renderMs}ms | boxes={count} | DPI={DpiScale:F2}");
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

        // Get cached brushes based on style
        SolidColorBrush bgBrush;
        SolidColorBrush textBrush;

        if (isBlackBorder)
        {
            bgBrush = BlackBgBrush;
            textBrush = WhiteTextBrush;
        }
        else if (isWhiteBorder)
        {
            bgBrush = WhiteBgBrush;
            textBrush = BlackTextBrush;
        }
        else
        {
            // Use group color with alpha for background
            bgBrush = GetOrCreateBgBrush(color.Color);
            textBrush = WhiteTextBrush;
        }

        // Get or create cached formatted text
        var formattedText = GetOrCreateFormattedText(label, textBrush);

        var textWidth = formattedText.Width + 8;  // Add padding
        var textHeight = formattedText.Height + 4;

        var labelY = y - textHeight - 2;
        if (labelY < 0) labelY = y + 2; // If above screen, show inside

        // Draw background rectangle
        var bgRect = new Rect(x, labelY, textWidth, textHeight);
        dc.DrawRoundedRectangle(bgBrush, null, bgRect, 3, 3);

        // Draw text
        dc.DrawText(formattedText, new Point(x + 4, labelY + 2));
    }

    /// <summary>
    /// Gets or creates a cached FormattedText for the label.
    /// </summary>
    private FormattedText GetOrCreateFormattedText(string label, SolidColorBrush textBrush)
    {
        // Cache key includes brush color to handle different styles
        var key = $"{label}_{textBrush.Color}";
        if (!_textCache.TryGetValue(key, out var text))
        {
            text = new FormattedText(
                label,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _labelTypeface,
                14,
                textBrush,
                _pixelsPerDip);
            _textCache[key] = text;
        }
        return text;
    }

    /// <summary>
    /// Gets or creates a cached background brush with alpha.
    /// </summary>
    private SolidColorBrush GetOrCreateBgBrush(Color baseColor)
    {
        var key = $"bg_{baseColor}";
        if (!_brushCache.TryGetValue(key, out var brush))
        {
            var bgColor = Color.FromArgb(200, baseColor.R, baseColor.G, baseColor.B);
            brush = new SolidColorBrush(bgColor);
            brush.Freeze();
            _brushCache[key] = brush;
        }
        return brush;
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
