using System.Diagnostics;
using System.Management;

namespace GamingVision.Utilities;

/// <summary>
/// Detects available GPUs using WMI.
/// </summary>
public static class GpuDetector
{
    /// <summary>
    /// Gets information about all available GPUs.
    /// </summary>
    public static List<GpuInfo> GetAvailableGpus()
    {
        var gpus = new List<GpuInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            using var results = searcher.Get();

            foreach (var item in results)
            {
                var obj = (ManagementObject)item;
                var name = obj["Name"]?.ToString() ?? "Unknown GPU";
                var adapterRam = obj["AdapterRAM"];
                var pnpDeviceId = obj["PNPDeviceID"]?.ToString() ?? "";

                Debug.WriteLine($"Found GPU: {name}, PNP: {pnpDeviceId}");

                // Skip virtual/software adapters
                if (name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    pnpDeviceId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"  Skipping virtual adapter: {name}");
                    continue;
                }

                // Determine vendor from name or PNP device ID
                var vendor = DetermineVendor(name, pnpDeviceId);

                // Get dedicated video memory
                ulong dedicatedMemory = 0;
                if (adapterRam != null)
                {
                    try
                    {
                        dedicatedMemory = Convert.ToUInt64(adapterRam);
                    }
                    catch
                    {
                        // Ignore conversion errors
                    }
                }

                // Try to get actual VRAM from registry for modern GPUs
                var actualVram = GetActualVramFromRegistry(pnpDeviceId);
                if (actualVram > dedicatedMemory)
                {
                    dedicatedMemory = actualVram;
                }

                Debug.WriteLine($"  Adding GPU: {name}, Vendor: {vendor}, VRAM: {dedicatedMemory}");

                gpus.Add(new GpuInfo
                {
                    Name = name,
                    Vendor = vendor,
                    DedicatedVideoMemory = dedicatedMemory
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error detecting GPUs via WMI: {ex.Message}");
            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        // If no GPUs found, return a default entry indicating the issue
        if (gpus.Count == 0)
        {
            Debug.WriteLine("No GPUs detected via WMI, GPU acceleration may still work");
        }

        return gpus;
    }

    /// <summary>
    /// Gets the primary (first non-Microsoft) GPU, or null if none found.
    /// </summary>
    public static GpuInfo? GetPrimaryGpu()
    {
        var gpus = GetAvailableGpus();
        return gpus.FirstOrDefault();
    }

    /// <summary>
    /// Checks if any GPU with DirectML support is available.
    /// </summary>
    public static bool IsDirectMLAvailable()
    {
        var gpu = GetPrimaryGpu();
        return gpu?.SupportsDirectML == true;
    }

    /// <summary>
    /// Gets a display string for the primary GPU.
    /// </summary>
    public static string GetPrimaryGpuDisplayString()
    {
        try
        {
            var gpu = GetPrimaryGpu();
            if (gpu == null)
            {
                // Even if WMI fails, DirectML might still work
                return "GPU detection unavailable (DirectML may still work)";
            }

            var directml = gpu.SupportsDirectML ? "DirectML" : "CPU fallback";
            return $"{gpu.Name} ({directml})";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in GetPrimaryGpuDisplayString: {ex.Message}");
            return "GPU detection error (DirectML may still work)";
        }
    }

    private static GpuVendor DetermineVendor(string name, string pnpDeviceId)
    {
        var combined = $"{name} {pnpDeviceId}".ToUpperInvariant();

        if (combined.Contains("NVIDIA") || combined.Contains("GEFORCE") || combined.Contains("QUADRO") || combined.Contains("RTX") || combined.Contains("GTX"))
            return GpuVendor.Nvidia;

        if (combined.Contains("AMD") || combined.Contains("RADEON") || combined.Contains("ATI"))
            return GpuVendor.Amd;

        if (combined.Contains("INTEL") || combined.Contains("UHD") || combined.Contains("IRIS"))
            return GpuVendor.Intel;

        if (combined.Contains("VEN_10DE"))
            return GpuVendor.Nvidia;

        if (combined.Contains("VEN_1002"))
            return GpuVendor.Amd;

        if (combined.Contains("VEN_8086"))
            return GpuVendor.Intel;

        return GpuVendor.Unknown;
    }

    private static ulong GetActualVramFromRegistry(string pnpDeviceId)
    {
        try
        {
            // Try to find the GPU in the registry to get actual VRAM
            // This is needed because WMI caps AdapterRAM at 4GB
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");

            if (key == null) return 0;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                if (!int.TryParse(subKeyName, out _)) continue;

                using var subKey = key.OpenSubKey(subKeyName);
                if (subKey == null) continue;

                var matchingId = subKey.GetValue("MatchingDeviceId")?.ToString() ?? "";

                // Check if this is our GPU
                if (!string.IsNullOrEmpty(pnpDeviceId) &&
                    pnpDeviceId.Contains(matchingId, StringComparison.OrdinalIgnoreCase))
                {
                    // Try different registry values for VRAM
                    var qwMemorySize = subKey.GetValue("HardwareInformation.qwMemorySize");
                    if (qwMemorySize is long memSize)
                    {
                        return (ulong)memSize;
                    }

                    var memorySize = subKey.GetValue("HardwareInformation.MemorySize");
                    if (memorySize is int intMemSize)
                    {
                        return (ulong)intMemSize;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading VRAM from registry: {ex.Message}");
        }

        return 0;
    }

    private static GpuInfo? DetectViaDirectX()
    {
        // Simple fallback - just try to create a D3D device and see what we get
        // This is a very basic approach
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Caption FROM Win32_DisplayConfiguration");
            using var results = searcher.Get();

            foreach (ManagementObject obj in results)
            {
                var caption = obj["Caption"]?.ToString();
                if (!string.IsNullOrEmpty(caption) &&
                    !caption.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                {
                    return new GpuInfo
                    {
                        Name = caption,
                        Vendor = DetermineVendor(caption, ""),
                        DedicatedVideoMemory = 0
                    };
                }
            }
        }
        catch
        {
            // Ignore fallback errors
        }

        return null;
    }
}
