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
    private const string CudaDownloadPageUrl = "https://developer.nvidia.com/cuda-13-0-0-download-archive";

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

    private void CudaDownload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = CudaDownloadPageUrl,
                UseShellExecute = true
            });

            MessageBox.Show(
                "Opening NVIDIA CUDA download page in your browser.\n\n" +
                "Download and install CUDA 13.0, then click 'Recheck Prerequisites' in the Training window.\n\n" +
                "Note: You may need to restart your computer after installation.",
                "Download CUDA 13.0",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open CUDA download page: {ex.Message}");
            MessageBox.Show(
                $"Failed to open browser.\n\nPlease visit:\n{CudaDownloadPageUrl}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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
