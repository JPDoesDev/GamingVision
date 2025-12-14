using GamingVision.Models;

namespace GamingVision.Services.Ocr;

/// <summary>
/// Interface for OCR (Optical Character Recognition) services.
/// </summary>
public interface IOcrService : IDisposable
{
    /// <summary>
    /// Gets whether the OCR service is ready to process images.
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// Initializes the OCR service.
    /// </summary>
    /// <param name="languageTag">Language tag (e.g., "en-US", "en").</param>
    /// <returns>True if initialization succeeded.</returns>
    Task<bool> InitializeAsync(string languageTag = "en");

    /// <summary>
    /// Extracts text from a region of captured frame data.
    /// </summary>
    /// <param name="frameData">Raw pixel data in BGRA format.</param>
    /// <param name="frameWidth">Width of the full frame.</param>
    /// <param name="frameHeight">Height of the full frame.</param>
    /// <param name="frameStride">Stride of the frame.</param>
    /// <param name="region">The region to extract text from.</param>
    /// <returns>Extracted text.</returns>
    Task<string> ExtractTextAsync(byte[] frameData, int frameWidth, int frameHeight, int frameStride, OcrRegion region);

    /// <summary>
    /// Extracts text from multiple regions of a captured frame.
    /// </summary>
    /// <param name="frameData">Raw pixel data in BGRA format.</param>
    /// <param name="frameWidth">Width of the full frame.</param>
    /// <param name="frameHeight">Height of the full frame.</param>
    /// <param name="frameStride">Stride of the frame.</param>
    /// <param name="regions">The regions to extract text from.</param>
    /// <returns>Dictionary mapping region labels to extracted text.</returns>
    Task<Dictionary<string, string>> ExtractTextFromRegionsAsync(
        byte[] frameData, int frameWidth, int frameHeight, int frameStride,
        IEnumerable<OcrRegion> regions);

    /// <summary>
    /// Gets available OCR languages on this system.
    /// </summary>
    IReadOnlyList<string> AvailableLanguages { get; }
}

/// <summary>
/// Represents a region to extract text from.
/// </summary>
public class OcrRegion
{
    /// <summary>
    /// Label/identifier for this region.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// X coordinate of the region (left).
    /// </summary>
    public int X { get; init; }

    /// <summary>
    /// Y coordinate of the region (top).
    /// </summary>
    public int Y { get; init; }

    /// <summary>
    /// Width of the region.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Height of the region.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Creates an OcrRegion from a DetectedObject.
    /// </summary>
    public static OcrRegion FromDetection(DetectedObject detection)
    {
        return new OcrRegion
        {
            Label = detection.Label,
            X = detection.X1,
            Y = detection.Y1,
            Width = detection.Width,
            Height = detection.Height
        };
    }
}

/// <summary>
/// Result of OCR processing for a single region.
/// </summary>
public class OcrResult
{
    /// <summary>
    /// The label of the region this text was extracted from.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// The extracted text.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Confidence score (0.0 to 1.0) if available.
    /// </summary>
    public float? Confidence { get; init; }

    /// <summary>
    /// The bounding box of the text region.
    /// </summary>
    public OcrRegion Region { get; init; } = new();
}
