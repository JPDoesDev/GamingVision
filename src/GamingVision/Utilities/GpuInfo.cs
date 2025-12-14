namespace GamingVision.Utilities;

/// <summary>
/// Information about a detected GPU.
/// </summary>
public class GpuInfo
{
    /// <summary>
    /// GPU device name/description.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Hardware vendor (NVIDIA, AMD, Intel, etc.).
    /// </summary>
    public GpuVendor Vendor { get; set; } = GpuVendor.Unknown;

    /// <summary>
    /// Dedicated video memory in bytes.
    /// </summary>
    public ulong DedicatedVideoMemory { get; set; }

    /// <summary>
    /// Dedicated video memory formatted as a string (e.g., "8 GB").
    /// </summary>
    public string DedicatedVideoMemoryFormatted => FormatBytes(DedicatedVideoMemory);

    /// <summary>
    /// Whether DirectML is expected to work well with this GPU.
    /// </summary>
    public bool SupportsDirectML => Vendor is GpuVendor.Nvidia or GpuVendor.Amd or GpuVendor.Intel;

    /// <summary>
    /// Friendly display string for the GPU.
    /// </summary>
    public string DisplayString => $"{Name} ({DedicatedVideoMemoryFormatted})";

    private static string FormatBytes(ulong bytes)
    {
        const double gb = 1024 * 1024 * 1024;
        const double mb = 1024 * 1024;

        if (bytes >= gb)
            return $"{bytes / gb:F1} GB";
        if (bytes >= mb)
            return $"{bytes / mb:F0} MB";
        return $"{bytes} bytes";
    }
}

/// <summary>
/// Known GPU vendors.
/// </summary>
public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
    Microsoft  // Software renderer
}
