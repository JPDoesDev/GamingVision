using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using GamingVision.Utilities;

namespace GamingVision.Services.Ocr;

/// <summary>
/// OCR service using Windows.Media.Ocr (WinRT).
/// Provides fast, GPU-accelerated text recognition.
/// </summary>
public class WindowsOcrService : IOcrService
{
    private OcrEngine? _ocrEngine;
    private bool _disposed;
    private List<string> _availableLanguages = [];

    public bool IsReady => _ocrEngine != null;
    public IReadOnlyList<string> AvailableLanguages => _availableLanguages;

    /// <summary>
    /// Initializes the OCR service with the specified language.
    /// </summary>
    public async Task<bool> InitializeAsync(string languageTag = "en")
    {
        return await Task.Run(() =>
        {
            try
            {
                // Get available languages
                _availableLanguages = OcrEngine.AvailableRecognizerLanguages
                    .Select(l => l.LanguageTag)
                    .ToList();

                Debug.WriteLine($"Available OCR languages: {string.Join(", ", _availableLanguages)}");

                // Try to find a matching language
                var matchingLanguage = OcrEngine.AvailableRecognizerLanguages
                    .FirstOrDefault(l => l.LanguageTag.StartsWith(languageTag, StringComparison.OrdinalIgnoreCase));

                if (matchingLanguage != null)
                {
                    _ocrEngine = OcrEngine.TryCreateFromLanguage(matchingLanguage);
                    if (_ocrEngine != null)
                    {
                        Debug.WriteLine($"OCR engine initialized with language: {matchingLanguage.LanguageTag}");
                        return true;
                    }
                }

                // Fall back to user profile languages
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (_ocrEngine != null)
                {
                    Debug.WriteLine("OCR engine initialized with user profile languages");
                    return true;
                }

                Debug.WriteLine("Failed to initialize OCR engine");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OCR initialization error: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Extracts text from a region of the captured frame.
    /// </summary>
    public async Task<string> ExtractTextAsync(
        byte[] frameData, int frameWidth, int frameHeight, int frameStride,
        OcrRegion region)
    {
        if (_ocrEngine == null)
        {
            Logger.Warn("ExtractTextAsync: OCR engine is null");
            return string.Empty;
        }

        try
        {
            // Add padding around the region to give OCR more context
            // Windows OCR works better with some margin around text
            const int padding = 10;

            int x = Math.Max(0, region.X - padding);
            int y = Math.Max(0, region.Y - padding);
            int x2 = Math.Min(frameWidth, region.X + region.Width + padding);
            int y2 = Math.Min(frameHeight, region.Y + region.Height + padding);
            int width = x2 - x;
            int height = y2 - y;

            Logger.Log($"ExtractTextAsync: Region '{region.Label}' original ({region.X},{region.Y})-({region.X + region.Width},{region.Y + region.Height}), padded ({x},{y})-({x2},{y2}), size {width}x{height}");

            // Create a cropped bitmap for the region
            var softwareBitmap = CreateCroppedBitmap(
                frameData, frameWidth, frameHeight, frameStride,
                x, y, width, height);

            if (softwareBitmap == null)
            {
                Logger.Warn("ExtractTextAsync: Failed to create cropped bitmap");
                return string.Empty;
            }

            // Run OCR
            var result = await _ocrEngine.RecognizeAsync(softwareBitmap);

            // Clean up
            softwareBitmap.Dispose();

            var text = result?.Text ?? string.Empty;
            Logger.Log($"ExtractTextAsync: OCR result for '{region.Label}': '{text}' (lines: {result?.Lines?.Count ?? 0})");

            return text;
        }
        catch (Exception ex)
        {
            Logger.Error($"ExtractTextAsync error for '{region.Label}'", ex);
            return string.Empty;
        }
    }

    /// <summary>
    /// Extracts text from multiple regions of the captured frame.
    /// </summary>
    public async Task<Dictionary<string, string>> ExtractTextFromRegionsAsync(
        byte[] frameData, int frameWidth, int frameHeight, int frameStride,
        IEnumerable<OcrRegion> regions)
    {
        var results = new Dictionary<string, string>();

        if (_ocrEngine == null)
            return results;

        // Process regions sequentially to avoid memory pressure
        foreach (var region in regions)
        {
            var text = await ExtractTextAsync(frameData, frameWidth, frameHeight, frameStride, region);
            if (!string.IsNullOrWhiteSpace(text))
            {
                results[region.Label] = text.Trim();
            }
        }

        return results;
    }

    /// <summary>
    /// Creates a cropped SoftwareBitmap from the source frame data.
    /// </summary>
    private static SoftwareBitmap? CreateCroppedBitmap(
        byte[] sourceData, int sourceWidth, int sourceHeight, int sourceStride,
        int cropX, int cropY, int cropWidth, int cropHeight)
    {
        try
        {
            // Extract the cropped region into a new byte array
            var croppedData = new byte[cropWidth * cropHeight * 4];
            int croppedStride = cropWidth * 4;

            for (int row = 0; row < cropHeight; row++)
            {
                int sourceOffset = (cropY + row) * sourceStride + cropX * 4;
                int destOffset = row * croppedStride;

                // Ensure we don't read past the source buffer
                if (sourceOffset + croppedStride <= sourceData.Length)
                {
                    Buffer.BlockCopy(sourceData, sourceOffset, croppedData, destOffset, croppedStride);
                }
            }

            // Create a SoftwareBitmap from the cropped data
            // Windows.Media.Ocr requires Bgra8 format
            var bitmap = new SoftwareBitmap(
                BitmapPixelFormat.Bgra8,
                cropWidth,
                cropHeight,
                BitmapAlphaMode.Premultiplied);

            bitmap.CopyFromBuffer(croppedData.AsBuffer());

            return bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating cropped bitmap: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // OcrEngine doesn't implement IDisposable, but we clear the reference
        _ocrEngine = null;

        GC.SuppressFinalize(this);
    }
}
