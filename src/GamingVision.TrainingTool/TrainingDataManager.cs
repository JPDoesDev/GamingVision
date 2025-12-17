using System.Drawing;
using System.Drawing.Imaging;
using GamingVision.Models;
using GamingVision.Services.ScreenCapture;

namespace GamingVision.TrainingTool;

/// <summary>
/// Manages training data folders and files for YOLO model training.
/// Outputs in LabelImg-compatible YOLO format.
/// </summary>
public class TrainingDataManager
{
    private readonly string _trainingDataRoot;
    private readonly string _gameId;
    private int _screenshotCounter;

    public string GameId => _gameId;
    public string ImagesPath => Path.Combine(_trainingDataRoot, _gameId, "images");
    public string LabelsPath => Path.Combine(_trainingDataRoot, _gameId, "labels");
    public string ClassesFile => Path.Combine(_trainingDataRoot, _gameId, "classes.txt");

    public TrainingDataManager(string trainingDataRoot, string gameId)
    {
        _trainingDataRoot = trainingDataRoot;
        _gameId = gameId;
        _screenshotCounter = 0;
    }

    /// <summary>
    /// Creates the training data directories if they don't exist.
    /// </summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ImagesPath);
        Directory.CreateDirectory(LabelsPath);
    }

    /// <summary>
    /// Initializes the screenshot counter based on existing files.
    /// </summary>
    public void InitializeCounter()
    {
        if (!Directory.Exists(ImagesPath))
        {
            _screenshotCounter = 0;
            return;
        }

        var existingFiles = Directory.GetFiles(ImagesPath, "screenshot_*.jpg");
        if (existingFiles.Length == 0)
        {
            _screenshotCounter = 0;
            return;
        }

        // Find the highest numbered screenshot
        int maxNumber = 0;
        foreach (var file in existingFiles)
        {
            var filename = Path.GetFileNameWithoutExtension(file);
            if (filename.StartsWith("screenshot_"))
            {
                var numberPart = filename.Substring("screenshot_".Length);
                if (int.TryParse(numberPart, out int number))
                {
                    maxNumber = Math.Max(maxNumber, number);
                }
            }
        }

        _screenshotCounter = maxNumber;
    }

    /// <summary>
    /// Gets the next sequential filename (without extension).
    /// </summary>
    public string GetNextFilename()
    {
        _screenshotCounter++;
        return $"screenshot_{_screenshotCounter:D4}";
    }

    /// <summary>
    /// Saves the classes.txt file for LabelImg compatibility.
    /// </summary>
    public void SaveClasses(IReadOnlyList<string> labels)
    {
        File.WriteAllLines(ClassesFile, labels);
    }

    /// <summary>
    /// Saves a captured frame as a JPEG image.
    /// </summary>
    public void SaveScreenshot(CapturedFrame frame, string filename)
    {
        var imagePath = Path.Combine(ImagesPath, filename + ".jpg");

        // Convert BGRA byte array to Bitmap and save
        using var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, frame.Width, frame.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            // Copy frame data to bitmap
            for (int y = 0; y < frame.Height; y++)
            {
                int srcOffset = y * frame.Stride;
                int dstOffset = y * bitmapData.Stride;

                unsafe
                {
                    byte* dst = (byte*)bitmapData.Scan0 + dstOffset;
                    for (int x = 0; x < frame.Width * 4; x++)
                    {
                        dst[x] = frame.Data[srcOffset + x];
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        bitmap.Save(imagePath, ImageFormat.Jpeg);
    }

    /// <summary>
    /// Saves YOLO-format annotations for a screenshot.
    /// Format: class_id center_x center_y width height (all normalized 0-1)
    /// </summary>
    public void SaveAnnotations(
        List<DetectedObject> detections,
        string filename,
        int imageWidth,
        int imageHeight,
        IReadOnlyList<string> labels)
    {
        var lines = new List<string>();
        var labelsList = labels.ToList();

        foreach (var det in detections)
        {
            int classId = labelsList.IndexOf(det.Label);
            if (classId < 0)
            {
                // Unknown label, skip
                continue;
            }

            // Convert pixel coords to normalized YOLO format
            float centerX = det.CenterX / imageWidth;
            float centerY = det.CenterY / imageHeight;
            float width = (float)det.Width / imageWidth;
            float height = (float)det.Height / imageHeight;

            // Clamp values to valid range
            centerX = Math.Clamp(centerX, 0f, 1f);
            centerY = Math.Clamp(centerY, 0f, 1f);
            width = Math.Clamp(width, 0f, 1f);
            height = Math.Clamp(height, 0f, 1f);

            lines.Add($"{classId} {centerX:F6} {centerY:F6} {width:F6} {height:F6}");
        }

        var labelsFile = Path.Combine(LabelsPath, filename + ".txt");
        File.WriteAllLines(labelsFile, lines);
    }

    /// <summary>
    /// Saves an empty annotation file (for manual labeling later).
    /// </summary>
    public void SaveEmptyAnnotations(string filename)
    {
        var labelsFile = Path.Combine(LabelsPath, filename + ".txt");
        File.WriteAllText(labelsFile, string.Empty);
    }

    /// <summary>
    /// Gets statistics about the current training data.
    /// </summary>
    public (int imageCount, int labeledCount) GetStatistics()
    {
        int imageCount = 0;
        int labeledCount = 0;

        if (Directory.Exists(ImagesPath))
        {
            imageCount = Directory.GetFiles(ImagesPath, "*.jpg").Length;
        }

        if (Directory.Exists(LabelsPath))
        {
            var labelFiles = Directory.GetFiles(LabelsPath, "*.txt");
            foreach (var file in labelFiles)
            {
                var content = File.ReadAllText(file).Trim();
                if (!string.IsNullOrEmpty(content))
                {
                    labeledCount++;
                }
            }
        }

        return (imageCount, labeledCount);
    }
}
