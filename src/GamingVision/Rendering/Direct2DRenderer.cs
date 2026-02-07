using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using GamingVision.Models;
using GamingVision.Utilities;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using DWriteFontStyle = Vortice.DirectWrite.FontStyle;
using DWriteFontStretch = Vortice.DirectWrite.FontStretch;

namespace GamingVision.Rendering;

/// <summary>
/// Hardware-accelerated unified overlay renderer using Direct2D.
/// Draws detection bounding boxes, labels, and crosshair in a single pass.
/// Replaces both OverlayRenderer and CrosshairRenderer.
/// </summary>
public class Direct2DRenderer : IDisposable
{
    // Direct2D resources
    private ID2D1Factory1? _d2dFactory;
    private ID2D1HwndRenderTarget? _renderTarget;
    private IDWriteFactory? _dwriteFactory;

    // Cached resources (all render-target-bound; must be recreated on device loss)
    private readonly Dictionary<string, ID2D1SolidColorBrush> _brushCache = new();
    private IDWriteTextFormat? _labelTextFormat;

    // Crosshair state
    private CrosshairSettings? _crosshairSettings;
    private bool _crosshairVisible;

    // Window dimensions (physical pixels)
    private IntPtr _hwnd;
    private int _width;
    private int _height;
    private bool _disposed;

    /// <summary>DPI scale factor (informational; not used for coordinate math).</summary>
    public double DpiScale { get; set; } = 1.0;

    /// <summary>
    /// Initializes Direct2D and DirectWrite resources for the given native window.
    /// Must be called from the thread that will perform rendering.
    /// </summary>
    public void Initialize(IntPtr hwnd, int width, int height)
    {
        _hwnd = hwnd;
        _width = width;
        _height = height;

        // Create D2D factory
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();

        // Create DirectWrite factory for text rendering
        _dwriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();

        // Create HWND render target with premultiplied alpha for DWM transparency
        CreateRenderTarget();

        // Create label text format: Segoe UI, 14pt (matching existing OverlayRenderer)
        _labelTextFormat = _dwriteFactory.CreateTextFormat(
            "Segoe UI",
            fontCollection: null!,
            DWriteFontWeight.Normal,
            DWriteFontStyle.Normal,
            DWriteFontStretch.Normal,
            14.0f * (96.0f / 72.0f)); // 14pt -> pixels at 96 DPI

        Logger.Log("D2DRenderer", $"Initialized: {width}x{height}");
    }

    /// <summary>
    /// Resizes the render target after window repositioning.
    /// </summary>
    public void Resize(int width, int height)
    {
        _width = width;
        _height = height;
        _renderTarget?.Resize(new SizeI(width, height));
    }

    /// <summary>
    /// Renders all detections and crosshair in a single frame.
    /// Must be called from the thread that created the render target.
    /// </summary>
    public void Render(
        IEnumerable<(DetectedObject detection, OverlayGroup group)>? detectionItems,
        ulong frameId = 0,
        long captureStartTicks = 0)
    {
        if (_renderTarget == null) return;

        var renderStartTicks = Stopwatch.GetTimestamp();

        _renderTarget.BeginDraw();
        _renderTarget.Clear(new Color4(0, 0, 0, 0)); // Fully transparent

        int count = 0;

        // Draw detection bounding boxes
        if (detectionItems != null)
        {
            foreach (var (det, group) in detectionItems)
            {
                DrawDetectionBox(det.X1, det.Y1, det.Width, det.Height, det.Label, group);
                count++;
            }
        }

        // Draw crosshair (if configured and visible)
        if (_crosshairVisible && _crosshairSettings != null)
        {
            DrawCrosshair(_crosshairSettings);
        }

        var result = _renderTarget.EndDraw();

        // Handle device loss
        if (result.Failure)
        {
            Logger.Warn($"D2DRenderer: EndDraw failed: 0x{result.Code:X8}, recreating resources");
            RecreateResources();
        }

        var renderMs = (double)(Stopwatch.GetTimestamp() - renderStartTicks) / Stopwatch.Frequency * 1000.0;
        if (frameId > 0)
        {
            Logger.PerfFrameTimed(frameId, captureStartTicks, "RENDER", $"D2D DrawAll ({renderMs:F1}ms, {count} boxes)");
        }
    }

    /// <summary>
    /// Clears detection boxes but preserves crosshair if visible.
    /// </summary>
    public void Clear()
    {
        if (_renderTarget == null) return;

        _renderTarget.BeginDraw();
        _renderTarget.Clear(new Color4(0, 0, 0, 0));

        if (_crosshairVisible && _crosshairSettings != null)
        {
            DrawCrosshair(_crosshairSettings);
        }

        var result = _renderTarget.EndDraw();
        if (result.Failure)
        {
            RecreateResources();
        }
    }

    #region Crosshair

    /// <summary>Updates crosshair settings (pass null to remove).</summary>
    public void UpdateCrosshairSettings(CrosshairSettings? settings)
    {
        _crosshairSettings = settings;
    }

    /// <summary>Sets crosshair visibility independently of detection overlay.</summary>
    public void SetCrosshairVisible(bool visible)
    {
        _crosshairVisible = visible;
    }

    private void DrawCrosshair(CrosshairSettings settings)
    {
        float centerX = _width / 2.0f + settings.OffsetX;
        float centerY = _height / 2.0f + settings.OffsetY;

        var fillBrush = GetOrCreateBrush(settings.Color);
        var outlineBrush = GetOrCreateBrush(settings.OutlineColor);

        float size = settings.Size;
        float thickness = settings.Thickness;
        float outlineThickness = settings.OutlineThickness;

        switch (settings.Shape.ToLowerInvariant())
        {
            case "circle":
                DrawCrosshairCircle(centerX, centerY, size, thickness, outlineThickness, fillBrush, outlineBrush);
                break;
            case "box":
                DrawCrosshairBox(centerX, centerY, size, thickness, outlineThickness, fillBrush, outlineBrush);
                break;
        }
    }

    private void DrawCrosshairCircle(float cx, float cy, float size, float thickness,
        float outlineThickness, ID2D1SolidColorBrush fillBrush, ID2D1SolidColorBrush outlineBrush)
    {
        float radius = size / 2.0f;
        float totalStroke = thickness + (outlineThickness * 2);

        // Outer outline
        if (outlineThickness > 0)
        {
            float outerDiameter = size + totalStroke;
            _renderTarget!.DrawEllipse(
                new Ellipse(new Vector2(cx, cy), outerDiameter / 2.0f, outerDiameter / 2.0f),
                outlineBrush, outlineThickness);

            // Inner outline
            float innerDiameter = size - thickness;
            _renderTarget.DrawEllipse(
                new Ellipse(new Vector2(cx, cy), innerDiameter / 2.0f, innerDiameter / 2.0f),
                outlineBrush, outlineThickness);
        }

        // Main circle
        _renderTarget!.DrawEllipse(
            new Ellipse(new Vector2(cx, cy), radius, radius),
            fillBrush, thickness);
    }

    private void DrawCrosshairBox(float cx, float cy, float size, float thickness,
        float outlineThickness, ID2D1SolidColorBrush fillBrush, ID2D1SolidColorBrush outlineBrush)
    {
        float halfSize = size / 2.0f;
        float totalStroke = thickness + (outlineThickness * 2);

        // Outer outline
        if (outlineThickness > 0)
        {
            float outerHalf = (size + totalStroke) / 2.0f;
            _renderTarget!.DrawRectangle(
                new RectangleF(cx - outerHalf, cy - outerHalf, outerHalf * 2, outerHalf * 2),
                outlineBrush, outlineThickness);

            // Inner outline
            float innerHalf = (size - thickness) / 2.0f;
            _renderTarget.DrawRectangle(
                new RectangleF(cx - innerHalf, cy - innerHalf, innerHalf * 2, innerHalf * 2),
                outlineBrush, outlineThickness);
        }

        // Main box
        _renderTarget!.DrawRectangle(
            new RectangleF(cx - halfSize, cy - halfSize, size, size),
            fillBrush, thickness);
    }

    #endregion

    #region Detection Drawing

    private void DrawDetectionBox(float x, float y, float width, float height, string label, OverlayGroup group)
    {
        var brush = GetOrCreateBrush(group.Color);
        var rect = new RectangleF(x, y, width, height);

        switch (group.Style.ToLowerInvariant())
        {
            case "highcontrastblack":
                DrawHighContrastBox(rect, brush, group.Thickness, isBlack: true);
                break;
            case "highcontrastwhite":
                DrawHighContrastBox(rect, brush, group.Thickness, isBlack: false);
                break;
            default: // "outlined"
                _renderTarget!.DrawRectangle(rect, brush, group.Thickness);
                break;
        }

        if (group.ShowLabel && !string.IsNullOrEmpty(label))
        {
            DrawLabel(x, y, label, group.Color, group.Style);
        }
    }

    private void DrawHighContrastBox(RectangleF rect, ID2D1SolidColorBrush fillBrush, int thickness, bool isBlack)
    {
        var borderBrush = isBlack
            ? GetOrCreateBrush("#000000")
            : GetOrCreateBrush("#FFFFFF");

        _renderTarget!.FillRectangle(rect, fillBrush);
        _renderTarget.DrawRectangle(rect, borderBrush, thickness);
    }

    private void DrawLabel(float x, float y, string label, string groupColor, string style)
    {
        var styleLower = style.ToLowerInvariant();

        // Determine background and text colors (matches existing OverlayRenderer logic)
        ID2D1SolidColorBrush bgBrush, textBrush;

        if (styleLower == "highcontrastblack")
        {
            bgBrush = GetOrCreateBrush("#C8000000");   // alpha=200, black
            textBrush = GetOrCreateBrush("#FFFFFF");
        }
        else if (styleLower == "highcontrastwhite")
        {
            bgBrush = GetOrCreateBrush("#C8FFFFFF");    // alpha=200, white
            textBrush = GetOrCreateBrush("#000000");
        }
        else
        {
            bgBrush = GetOrCreateBgBrush(groupColor);
            textBrush = GetOrCreateBrush("#FFFFFF");
        }

        // Measure text using DirectWrite text layout
        using var textLayout = _dwriteFactory!.CreateTextLayout(
            label, _labelTextFormat!, float.MaxValue, float.MaxValue);

        var metrics = textLayout.Metrics;
        float textWidth = metrics.Width + 8;    // +8 padding (matching existing)
        float textHeight = metrics.Height + 4;  // +4 padding

        float labelY = y - textHeight - 2;
        if (labelY < 0) labelY = y + 2; // If above screen, show inside

        // Draw background rounded rectangle
        var bgRect = new RoundedRectangle
        {
            Rect = new RectangleF(x, labelY, textWidth, textHeight),
            RadiusX = 3,
            RadiusY = 3
        };
        _renderTarget!.FillRoundedRectangle(bgRect, bgBrush);

        // Draw text (DrawText expects Vortice.Mathematics.Rect)
        var textRect = new Rect(x + 4, labelY + 2, textWidth - 4, textHeight - 2);
        _renderTarget.DrawText(label, _labelTextFormat!, textRect, textBrush);
    }

    #endregion

    #region Resource Management

    private ID2D1SolidColorBrush GetOrCreateBrush(string hexColor)
    {
        if (_brushCache.TryGetValue(hexColor, out var brush))
            return brush;

        var color = ParseHexColor(hexColor);
        brush = _renderTarget!.CreateSolidColorBrush(color);
        _brushCache[hexColor] = brush;
        return brush;
    }

    private ID2D1SolidColorBrush GetOrCreateBgBrush(string groupHexColor)
    {
        var key = $"bg_{groupHexColor}";
        if (_brushCache.TryGetValue(key, out var brush))
            return brush;

        var baseColor = ParseHexColor(groupHexColor);
        var bgColor = new Color4(baseColor.R, baseColor.G, baseColor.B, 200.0f / 255.0f);
        brush = _renderTarget!.CreateSolidColorBrush(bgColor);
        _brushCache[key] = brush;
        return brush;
    }

    private static Color4 ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        try
        {
            if (hex.Length == 6)
            {
                float r = Convert.ToByte(hex[..2], 16) / 255.0f;
                float g = Convert.ToByte(hex[2..4], 16) / 255.0f;
                float b = Convert.ToByte(hex[4..6], 16) / 255.0f;
                return new Color4(r, g, b, 1.0f);
            }
            if (hex.Length == 8)
            {
                float a = Convert.ToByte(hex[..2], 16) / 255.0f;
                float r = Convert.ToByte(hex[2..4], 16) / 255.0f;
                float g = Convert.ToByte(hex[4..6], 16) / 255.0f;
                float b = Convert.ToByte(hex[6..8], 16) / 255.0f;
                return new Color4(r, g, b, a);
            }
        }
        catch { }
        return new Color4(1.0f, 0, 0, 1.0f); // Fallback: red
    }

    private void CreateRenderTarget()
    {
        var renderTargetProperties = new RenderTargetProperties
        {
            Type = RenderTargetType.Default,
            PixelFormat = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            DpiX = 96.0f,
            DpiY = 96.0f,
            Usage = RenderTargetUsage.None,
            MinLevel = FeatureLevel.Default
        };

        var hwndRenderTargetProperties = new HwndRenderTargetProperties
        {
            Hwnd = _hwnd,
            PixelSize = new SizeI(_width, _height),
            PresentOptions = PresentOptions.Immediately
        };

        _renderTarget = _d2dFactory!.CreateHwndRenderTarget(renderTargetProperties, hwndRenderTargetProperties);
    }

    private void RecreateResources()
    {
        // Dispose all device-dependent resources (brushes are render-target-bound)
        foreach (var brush in _brushCache.Values)
            brush.Dispose();
        _brushCache.Clear();

        _renderTarget?.Dispose();

        try
        {
            CreateRenderTarget();
            Logger.Log("D2DRenderer", "Render target recreated after device loss");
        }
        catch (Exception ex)
        {
            Logger.Error("D2DRenderer: Failed to recreate render target", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var brush in _brushCache.Values)
            brush.Dispose();
        _brushCache.Clear();

        _labelTextFormat?.Dispose();
        _renderTarget?.Dispose();
        _dwriteFactory?.Dispose();
        _d2dFactory?.Dispose();

        GC.SuppressFinalize(this);
    }

    #endregion
}
