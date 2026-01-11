using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using GamingVision.Utilities;

namespace GamingVision.Views;

/// <summary>
/// Dialog showing training prerequisites status with install/download options.
/// </summary>
public partial class PrerequisitesDialog : Window
{
    private const string PythonInstallerUrl = "https://www.python.org/ftp/python/3.10.0/python-3.10.0-amd64.exe";
    private const string PythonInstallerName = "python-3.10.0-amd64.exe";
    private const string CudaInstallerUrl = "https://developer.download.nvidia.com/compute/cuda/13.0.0/local_installers/cuda_13.0.0_553.05_windows.exe";
    private const string CudaInstallerName = "cuda_13.0.0_553.05_windows.exe";

    /// <summary>
    /// Gets whether the user chose to continue with CPU training.
    /// </summary>
    public bool ContinueWithCpu { get; private set; }

    /// <summary>
    /// Action to install PyTorch (set by caller).
    /// </summary>
    public Action? InstallPyTorchAction { get; set; }

    /// <summary>
    /// Action to install packages (set by caller).
    /// </summary>
    public Action? InstallPackagesAction { get; set; }

    public PrerequisitesDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the status for Python 3.10.
    /// </summary>
    public void SetPythonStatus(bool passed, string status)
    {
        PythonIcon.Text = passed ? "[OK]" : "[X]";
        PythonIcon.Foreground = passed ? Brushes.LimeGreen : Brushes.OrangeRed;
        PythonStatus.Text = $"Python 3.10: {status}";
        PythonDownloadBtn.Visibility = passed ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Sets the status for CUDA 13.0.
    /// </summary>
    public void SetCudaStatus(bool passed, string status)
    {
        CudaIcon.Text = passed ? "[OK]" : "[X]";
        CudaIcon.Foreground = passed ? Brushes.LimeGreen : Brushes.OrangeRed;
        CudaStatus.Text = $"CUDA 13.0: {status}";
        CudaDownloadBtn.Visibility = passed ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Sets the status for PyTorch.
    /// </summary>
    public void SetPytorchStatus(bool passed, string status)
    {
        PytorchIcon.Text = passed ? "[OK]" : "[X]";
        PytorchIcon.Foreground = passed ? Brushes.LimeGreen : Brushes.OrangeRed;
        PytorchStatus.Text = $"PyTorch: {status}";
        PytorchInstallBtn.Visibility = passed ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Sets the status for required packages.
    /// </summary>
    public void SetPackagesStatus(bool passed, string status)
    {
        PackagesIcon.Text = passed ? "[OK]" : "[X]";
        PackagesIcon.Foreground = passed ? Brushes.LimeGreen : Brushes.OrangeRed;
        PackagesStatus.Text = $"Packages: {status}";
        PackagesInstallBtn.Visibility = passed ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void PythonDownload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Disable button during download/install
            PythonDownloadBtn.IsEnabled = false;
            PythonDownloadBtn.Content = "Downloading...";

            var tempPath = Path.Combine(Path.GetTempPath(), PythonInstallerName);

            // Download the installer
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(10);
                var response = await client.GetAsync(PythonInstallerUrl);
                response.EnsureSuccessStatusCode();

                await using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);
            }

            Logger.Log($"Python installer downloaded to: {tempPath}");
            PythonDownloadBtn.Content = "Installing...";

            // Run the installer with silent install parameters
            var psi = new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "InstallAllUsers=1 PrependPath=1 Include_pip=1 TargetDir=C:\\Python310",
                UseShellExecute = true,
                Verb = "runas" // Request admin elevation
            };

            var process = Process.Start(psi);
            if (process != null)
            {
                MessageBox.Show(
                    "Python 3.10 installer has started.\n\n" +
                    "Please wait for the installation to complete, then click 'Recheck Prerequisites' in the Training window.\n\n" +
                    "Note: You may need to restart your computer for PATH changes to take effect.",
                    "Installing Python 3.10",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to download/install Python: {ex.Message}");
            MessageBox.Show(
                $"Failed to download or install Python:\n{ex.Message}\n\n" +
                "Please download manually from:\nhttps://www.python.org/downloads/release/python-3100/",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            PythonDownloadBtn.IsEnabled = true;
            PythonDownloadBtn.Content = "Install Python 3.10";
        }
    }

    private async void CudaDownload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Disable button during download/install
            CudaDownloadBtn.IsEnabled = false;
            CudaDownloadBtn.Content = "Downloading...";

            var tempPath = Path.Combine(Path.GetTempPath(), CudaInstallerName);

            // Download the installer (CUDA is ~3GB, so this will take a while)
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(60); // CUDA is large, allow more time

                // Use streaming to handle large file
                using var response = await client.GetAsync(CudaInstallerUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var totalMB = totalBytes / (1024.0 * 1024.0);

                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    // Update progress every 10MB
                    if (totalBytes > 0 && totalRead % (10 * 1024 * 1024) < 8192)
                    {
                        var percent = (int)((totalRead * 100) / totalBytes);
                        CudaDownloadBtn.Content = $"Downloading... {percent}%";
                    }
                }
            }

            Logger.Log($"CUDA installer downloaded to: {tempPath}");
            CudaDownloadBtn.Content = "Installing...";

            // Run the installer with silent install parameters
            var psi = new ProcessStartInfo
            {
                FileName = tempPath,
                Arguments = "-s", // Silent install
                UseShellExecute = true,
                Verb = "runas" // Request admin elevation
            };

            var process = Process.Start(psi);
            if (process != null)
            {
                MessageBox.Show(
                    "CUDA 13.0 installer has started (silent install).\n\n" +
                    "This may take several minutes. Please wait for the installation to complete, " +
                    "then click 'Recheck Prerequisites' in the Training window.\n\n" +
                    "Note: You may need to restart your computer after installation.",
                    "Installing CUDA 13.0",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to download/install CUDA: {ex.Message}");
            MessageBox.Show(
                $"Failed to download or install CUDA:\n{ex.Message}\n\n" +
                "Please download manually from:\nhttps://developer.nvidia.com/cuda-13-0-0-download-archive",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            CudaDownloadBtn.IsEnabled = true;
            CudaDownloadBtn.Content = "Install CUDA 13.0";
        }
    }

    private void PytorchInstall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Launch visible terminal with /k to keep it open after command completes
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k pip install torch torchvision --index-url https://download.pytorch.org/whl/cu130",
                UseShellExecute = true
            };

            Process.Start(psi);

            MessageBox.Show(
                "Terminal opened to install PyTorch with CUDA 13.0.\n\n" +
                "Close the terminal when done, then click 'Recheck Prerequisites' in the Training window.",
                "Installing PyTorch",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open terminal:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PackagesInstall_Click(object sender, RoutedEventArgs e)
    {
        if (InstallPackagesAction != null)
        {
            InstallPackagesAction();
            Close();
        }
        else
        {
            MessageBox.Show(
                "To install required packages, run:\n\n" +
                "pip install -r requirements.txt\n\n" +
                "Then restart the training window.",
                "Install Packages",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void ContinueCpu_Click(object sender, RoutedEventArgs e)
    {
        ContinueWithCpu = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ContinueWithCpu = false;
        DialogResult = false;
        Close();
    }
}
