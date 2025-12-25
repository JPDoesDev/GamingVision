using System.Diagnostics;
using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using GamingVision.Models;
using GamingVision.Services.ScreenCapture;
using GamingVision.Utilities;

namespace GamingVision.Services.Detection;

/// <summary>
/// YOLO object detection service using ONNX Runtime.
/// Supports YOLOv11 models exported to ONNX format.
/// </summary>
public class YoloDetectionService : IDetectionService
{
    private InferenceSession? _session;
    private string[] _labels = [];
    private int _inputWidth = 640;
    private int _inputHeight = 640;
    private string _inputName = "images";
    private string _outputName = "output0";
    private bool _disposed;
    private readonly object _inferenceLock = new();
    private bool _isInferenceRunning;

    // Reusable buffers to avoid per-frame allocations
    private float[]? _tensorBuffer;
    private byte[]? _frameBuffer;
    private NamedOnnxValue[]? _inputsArray;

    public bool IsReady => _session != null;
    public IReadOnlyList<string> Labels => _labels;
    public string ExecutionProvider { get; private set; } = "Unknown";

    /// <summary>
    /// Initializes the YOLO detection service with a model file.
    /// </summary>
    /// <param name="modelPath">Path to the ONNX model file.</param>
    /// <param name="useGpu">Whether to use GPU acceleration via DirectML.</param>
    public async Task<bool> InitializeAsync(string modelPath, bool useGpu = true)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Check if model file exists
                if (!File.Exists(modelPath))
                {
                    Debug.WriteLine($"Model file not found: {modelPath}");
                    return false;
                }

                // Create session options with performance optimizations
                var sessionOptions = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    EnableMemoryPattern = true,      // Reuse memory allocations between runs
                    EnableCpuMemArena = true,        // Use arena allocator for CPU tensors
                    ExecutionMode = ExecutionMode.ORT_PARALLEL,  // Parallel execution for operators
                };

                // Set thread count for CPU operations (post-processing)
                sessionOptions.IntraOpNumThreads = Environment.ProcessorCount;

                // Try GPU first, fall back to CPU
                if (useGpu)
                {
                    try
                    {
                        // Use DirectML for GPU acceleration (works with NVIDIA, AMD, Intel)
                        sessionOptions.AppendExecutionProvider_DML(0);
                        ExecutionProvider = "DirectML (GPU)";
                        Debug.WriteLine("Using DirectML execution provider");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"DirectML not available: {ex.Message}. Falling back to CPU.");
                        ExecutionProvider = "CPU";
                    }
                }
                else
                {
                    ExecutionProvider = "CPU";
                }

                // Load the model
                _session = new InferenceSession(modelPath, sessionOptions);

                // Get input/output metadata
                var inputMeta = _session.InputMetadata;
                var outputMeta = _session.OutputMetadata;

                if (inputMeta.Count > 0)
                {
                    var firstInput = inputMeta.First();
                    _inputName = firstInput.Key;
                    var dims = firstInput.Value.Dimensions;
                    if (dims.Length >= 4)
                    {
                        // NCHW format: [batch, channels, height, width]
                        _inputHeight = dims[2] > 0 ? dims[2] : 640;
                        _inputWidth = dims[3] > 0 ? dims[3] : 640;
                    }
                }

                if (outputMeta.Count > 0)
                {
                    _outputName = outputMeta.First().Key;
                }

                Debug.WriteLine($"Model loaded: input={_inputName} ({_inputWidth}x{_inputHeight}), output={_outputName}");
                Logger.Log($"YOLO model input size: {_inputWidth}x{_inputHeight}");

                // Pre-allocate reusable buffers now that we know dimensions
                int tensorSize = 3 * _inputHeight * _inputWidth;
                _tensorBuffer = new float[tensorSize];
                _inputsArray = new NamedOnnxValue[1];
                Debug.WriteLine($"Pre-allocated tensor buffer: {tensorSize * 4 / 1024}KB");

                // Try to load labels from accompanying file
                LoadLabels(modelPath);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize YOLO model: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Loads labels from a .txt file with the same name as the model.
    /// </summary>
    private void LoadLabels(string modelPath)
    {
        var labelsPath = Path.ChangeExtension(modelPath, ".txt");
        if (File.Exists(labelsPath))
        {
            _labels = File.ReadAllLines(labelsPath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
            Debug.WriteLine($"Loaded {_labels.Length} labels from {labelsPath}");
        }
        else
        {
            // Try modelname_labels.txt
            var dir = Path.GetDirectoryName(modelPath) ?? ".";
            var name = Path.GetFileNameWithoutExtension(modelPath);
            labelsPath = Path.Combine(dir, $"{name}_labels.txt");

            if (File.Exists(labelsPath))
            {
                _labels = File.ReadAllLines(labelsPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToArray();
                Debug.WriteLine($"Loaded {_labels.Length} labels from {labelsPath}");
            }
            else
            {
                Debug.WriteLine("No labels file found, using numeric class IDs");
                _labels = [];
            }
        }
    }

    /// <summary>
    /// Runs object detection on a captured frame.
    /// </summary>
    public async Task<List<DetectedObject>> DetectAsync(CapturedFrame frame, float confidenceThreshold = 0.5f)
    {
        // Capture session reference early to prevent null access if disposed during async operation
        // Early exit if disposed or disposing
        if (_disposed) return [];

        var session = _session;
        if (session == null || frame.IsDisposed)
        {
            Logger.Warn("DetectAsync: Session null or frame disposed");
            return [];
        }

        // Skip if inference is already running (prevents concurrent ONNX calls which can crash)
        lock (_inferenceLock)
        {
            if (_isInferenceRunning || _disposed)
            {
                // Return null to indicate "skipped" vs empty list for "no detections found"
                return null!;
            }
            _isInferenceRunning = true;
        }

        try
        {
            var totalSw = Stopwatch.StartNew();

            // Reuse frame buffer if large enough, otherwise allocate (handles resolution changes)
            var frameSize = frame.Data.Length;
            if (_frameBuffer == null || _frameBuffer.Length < frameSize)
            {
                _frameBuffer = new byte[frameSize];
            }

            // Copy frame data to reusable buffer
            var copySw = Stopwatch.StartNew();
            Buffer.BlockCopy(frame.Data, 0, _frameBuffer, 0, frameSize);
            var copyMs = copySw.ElapsedMilliseconds;

            var frameWidth = frame.Width;
            var frameHeight = frame.Height;
            var frameStride = frame.Stride;

            // Capture references for closure
            var tensorBuffer = _tensorBuffer!;
            var inputsArray = _inputsArray!;
            var frameBuffer = _frameBuffer;
            var inputWidth = _inputWidth;
            var inputHeight = _inputHeight;

            return await Task.Run(() =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();

                    // Preprocess: Convert BGRA to RGB and resize to model input size (uses reusable buffer)
                    var inputTensor = PreprocessFrameDataReusable(frameBuffer, frameWidth, frameHeight, frameStride, tensorBuffer);
                    var preprocessMs = sw.ElapsedMilliseconds;
                    sw.Restart();

                    // Run inference using reusable inputs array
                    inputsArray[0] = NamedOnnxValue.CreateFromTensor(_inputName, inputTensor);
                    using var results = session.Run(inputsArray);
                    var inferenceMs = sw.ElapsedMilliseconds;
                    sw.Restart();

                    var outputTensor = results.First().AsTensor<float>();

                    // Post-process: Extract detections from YOLO output
                    var detections = PostProcessYoloOutput(
                        outputTensor,
                        frameWidth,
                        frameHeight,
                        confidenceThreshold);

                    // Apply NMS
                    var finalDetections = ApplyNms(detections, 0.45f);
                    var postprocessMs = sw.ElapsedMilliseconds;

                    var totalMs = totalSw.ElapsedMilliseconds;

                    // Log performance metrics
                    Logger.Log($"[PERF] Detection: copy={copyMs}ms, preprocess={preprocessMs}ms, inference={inferenceMs}ms, postprocess={postprocessMs}ms, TOTAL={totalMs}ms | model={inputWidth}x{inputHeight}, detections={finalDetections.Count}");

                    return finalDetections;
                }
                catch (Exception ex)
                {
                    Logger.Error("DetectAsync error", ex);
                    return [];
                }
            });
        }
        finally
        {
            lock (_inferenceLock)
            {
                _isInferenceRunning = false;
            }
        }
    }

    /// <summary>
    /// Preprocesses raw frame data for YOLO inference.
    /// Converts BGRA to RGB, resizes, and normalizes to [0, 1].
    /// Uses parallel processing for performance.
    /// </summary>
    private DenseTensor<float> PreprocessFrameData(byte[] data, int width, int height, int stride)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, _inputHeight, _inputWidth });

        // Calculate scaling factors
        float scaleX = (float)width / _inputWidth;
        float scaleY = (float)height / _inputHeight;

        // Pre-compute the inverse for multiplication instead of division (faster)
        const float inv255 = 1f / 255f;

        // Get underlying array for use in parallel loop (Span can't be captured in lambdas)
        var tensorArray = tensor.Buffer.ToArray();
        int channelSize = _inputHeight * _inputWidth;
        int inputWidth = _inputWidth;
        int inputHeight = _inputHeight;

        // Process rows in parallel for significant speedup
        Parallel.For(0, inputHeight, y =>
        {
            // Pre-calculate source row offset
            int srcY = Math.Min((int)(y * scaleY), height - 1);
            int srcRowOffset = srcY * stride;
            int tensorRowOffset = y * inputWidth;

            for (int x = 0; x < inputWidth; x++)
            {
                // Map to source coordinates
                int srcX = Math.Min((int)(x * scaleX), width - 1);

                // Calculate source pixel offset (BGRA format)
                int srcOffset = srcRowOffset + srcX * 4;

                // Bounds check to prevent array index out of range
                if (srcOffset + 2 >= data.Length)
                    continue;

                // Extract BGR values and convert to RGB, normalize to [0, 1]
                // Write directly to tensor buffer (NCHW layout: R channel, then G, then B)
                int pixelIndex = tensorRowOffset + x;
                tensorArray[pixelIndex] = data[srcOffset + 2] * inv255;                    // R channel
                tensorArray[channelSize + pixelIndex] = data[srcOffset + 1] * inv255;      // G channel
                tensorArray[2 * channelSize + pixelIndex] = data[srcOffset] * inv255;      // B channel
            }
        });

        // Copy array back to tensor
        var tensorSpan = tensor.Buffer.Span;
        tensorArray.AsSpan().CopyTo(tensorSpan);

        return tensor;
    }

    /// <summary>
    /// Preprocesses frame data using a reusable buffer to avoid allocations.
    /// Writes directly to the provided tensor buffer.
    /// </summary>
    private DenseTensor<float> PreprocessFrameDataReusable(byte[] data, int width, int height, int stride, float[] tensorBuffer)
    {
        // Calculate scaling factors
        float scaleX = (float)width / _inputWidth;
        float scaleY = (float)height / _inputHeight;

        const float inv255 = 1f / 255f;
        int channelSize = _inputHeight * _inputWidth;
        int inputWidth = _inputWidth;
        int inputHeight = _inputHeight;

        // Process rows in parallel, writing directly to reusable buffer
        Parallel.For(0, inputHeight, y =>
        {
            int srcY = Math.Min((int)(y * scaleY), height - 1);
            int srcRowOffset = srcY * stride;
            int tensorRowOffset = y * inputWidth;

            for (int x = 0; x < inputWidth; x++)
            {
                int srcX = Math.Min((int)(x * scaleX), width - 1);
                int srcOffset = srcRowOffset + srcX * 4;

                if (srcOffset + 2 >= data.Length)
                    continue;

                int pixelIndex = tensorRowOffset + x;
                tensorBuffer[pixelIndex] = data[srcOffset + 2] * inv255;                    // R
                tensorBuffer[channelSize + pixelIndex] = data[srcOffset + 1] * inv255;      // G
                tensorBuffer[2 * channelSize + pixelIndex] = data[srcOffset] * inv255;      // B
            }
        });

        // Create tensor using the pre-filled buffer (no copy - tensor uses the array directly)
        return new DenseTensor<float>(tensorBuffer, new[] { 1, 3, _inputHeight, _inputWidth });
    }

    /// <summary>
    /// Post-processes YOLO output tensor to extract detections.
    /// YOLO output format: [1, num_classes + 4, num_boxes] where boxes are [x_center, y_center, width, height]
    /// </summary>
    private List<DetectedObject> PostProcessYoloOutput(
        Tensor<float> output,
        int originalWidth,
        int originalHeight,
        float confidenceThreshold)
    {
        var detections = new List<DetectedObject>();
        var dims = output.Dimensions.ToArray();

        // YOLO format: [1, 4 + num_classes, num_boxes]
        // Need to transpose to [1, num_boxes, 4 + num_classes]
        int numFeatures = dims[1]; // 4 + num_classes
        int numBoxes = dims[2];
        int numClasses = numFeatures - 4;

        if (numClasses <= 0)
        {
            Debug.WriteLine($"Invalid output dimensions: features={numFeatures}, boxes={numBoxes}");
            return detections;
        }

        // Scale factors to convert from model input size to original frame size
        float scaleX = (float)originalWidth / _inputWidth;
        float scaleY = (float)originalHeight / _inputHeight;

        for (int i = 0; i < numBoxes; i++)
        {
            // Extract box coordinates (x_center, y_center, width, height)
            float xCenter = output[0, 0, i];
            float yCenter = output[0, 1, i];
            float boxWidth = output[0, 2, i];
            float boxHeight = output[0, 3, i];

            // Find the class with highest confidence
            int bestClass = 0;
            float bestConfidence = 0;

            for (int c = 0; c < numClasses; c++)
            {
                float confidence = output[0, 4 + c, i];
                if (confidence > bestConfidence)
                {
                    bestConfidence = confidence;
                    bestClass = c;
                }
            }

            // Skip low-confidence detections
            if (bestConfidence < confidenceThreshold)
                continue;

            // Convert to corner coordinates and scale to original size
            float x1 = (xCenter - boxWidth / 2) * scaleX;
            float y1 = (yCenter - boxHeight / 2) * scaleY;
            float x2 = (xCenter + boxWidth / 2) * scaleX;
            float y2 = (yCenter + boxHeight / 2) * scaleY;

            // Clamp to image bounds
            x1 = Math.Max(0, Math.Min(x1, originalWidth));
            y1 = Math.Max(0, Math.Min(y1, originalHeight));
            x2 = Math.Max(0, Math.Min(x2, originalWidth));
            y2 = Math.Max(0, Math.Min(y2, originalHeight));

            detections.Add(new DetectedObject
            {
                Label = bestClass < _labels.Length ? _labels[bestClass] : $"class_{bestClass}",
                Confidence = bestConfidence,
                X1 = (int)x1,
                Y1 = (int)y1,
                X2 = (int)x2,
                Y2 = (int)y2
            });
        }

        return detections;
    }

    /// <summary>
    /// Applies Non-Maximum Suppression to remove overlapping detections.
    /// </summary>
    private static List<DetectedObject> ApplyNms(List<DetectedObject> detections, float iouThreshold)
    {
        if (detections.Count == 0)
            return detections;

        // Sort by confidence (descending)
        var sorted = detections.OrderByDescending(d => d.Confidence).ToList();
        var result = new List<DetectedObject>();

        while (sorted.Count > 0)
        {
            var best = sorted[0];
            result.Add(best);
            sorted.RemoveAt(0);

            // Remove boxes that overlap too much with the best box
            sorted.RemoveAll(d =>
                d.Label == best.Label &&
                CalculateIoU(best, d) > iouThreshold);
        }

        return result;
    }

    /// <summary>
    /// Calculates Intersection over Union (IoU) between two detections.
    /// </summary>
    private static float CalculateIoU(DetectedObject a, DetectedObject b)
    {
        int x1 = Math.Max(a.X1, b.X1);
        int y1 = Math.Max(a.Y1, b.Y1);
        int x2 = Math.Min(a.X2, b.X2);
        int y2 = Math.Min(a.Y2, b.Y2);

        int intersectionWidth = Math.Max(0, x2 - x1);
        int intersectionHeight = Math.Max(0, y2 - y1);
        float intersection = intersectionWidth * intersectionHeight;

        float areaA = a.Width * a.Height;
        float areaB = b.Width * b.Height;
        float union = areaA + areaB - intersection;

        return union > 0 ? intersection / union : 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Wait for any running inference to complete before disposing the session
        // This prevents crashes when stopping while inference is in progress
        var spinWait = new SpinWait();
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (_isInferenceRunning && DateTime.UtcNow < timeout)
        {
            spinWait.SpinOnce();
        }

        _session?.Dispose();
        _session = null;

        GC.SuppressFinalize(this);
    }
}
