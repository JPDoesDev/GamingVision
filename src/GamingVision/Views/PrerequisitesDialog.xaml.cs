using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace GamingVision.Views;

/// <summary>
/// Dialog showing training prerequisites status with install/download options.
/// </summary>
public partial class PrerequisitesDialog : Window
{
    private const string PythonDownloadUrl = "https://www.python.org/downloads/release/python-3100/";
    private const string CudaDownloadUrl = "https://developer.nvidia.com/cuda-13-0-0-download-archive";

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

    private void PythonDownload_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(PythonDownloadUrl);
    }

    private void CudaDownload_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(CudaDownloadUrl);
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

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show($"Failed to open browser. Please visit:\n{url}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
