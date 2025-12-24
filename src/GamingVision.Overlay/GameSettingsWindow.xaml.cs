using System.IO;
using System.Windows;
using System.Windows.Forms;
using GamingVision.Models;

namespace GamingVision.Overlay;

/// <summary>
/// Game settings window for configuring per-game settings relevant to the overlay application.
/// </summary>
public partial class GameSettingsWindow : Window
{
    private readonly GameProfile _profile;
    private readonly string _gameDirectory;

    public GameSettingsWindow(GameProfile profile, string gameDirectory)
    {
        InitializeComponent();

        _profile = profile;
        _gameDirectory = gameDirectory;

        LoadSettings();
    }

    private void LoadSettings()
    {
        // Load display name
        DisplayNameTextBox.Text = _profile.DisplayName;

        // Load available model files
        LoadAvailableModels();

        // Load monitor options
        LoadMonitorOptions();
    }

    private void LoadAvailableModels()
    {
        ModelFileComboBox.Items.Clear();

        if (Directory.Exists(_gameDirectory))
        {
            var onnxFiles = Directory.GetFiles(_gameDirectory, "*.onnx");
            foreach (var file in onnxFiles)
            {
                var fileName = Path.GetFileName(file);
                ModelFileComboBox.Items.Add(fileName);
            }
        }

        // Select current model
        if (!string.IsNullOrEmpty(_profile.ModelFile))
        {
            var index = ModelFileComboBox.Items.IndexOf(_profile.ModelFile);
            if (index >= 0)
            {
                ModelFileComboBox.SelectedIndex = index;
            }
            else if (ModelFileComboBox.Items.Count > 0)
            {
                // Model not found, add it anyway so user can see what's configured
                ModelFileComboBox.Items.Add(_profile.ModelFile);
                ModelFileComboBox.SelectedItem = _profile.ModelFile;
            }
        }
        else if (ModelFileComboBox.Items.Count > 0)
        {
            ModelFileComboBox.SelectedIndex = 0;
        }
    }

    private void LoadMonitorOptions()
    {
        MonitorComboBox.Items.Clear();

        // Get actual monitor count
        var screenCount = Screen.AllScreens.Length;

        for (int i = 0; i < screenCount; i++)
        {
            var screen = Screen.AllScreens[i];
            var label = screen.Primary
                ? $"Monitor {i + 1} (Primary) - {screen.Bounds.Width}x{screen.Bounds.Height}"
                : $"Monitor {i + 1} - {screen.Bounds.Width}x{screen.Bounds.Height}";
            MonitorComboBox.Items.Add(label);
        }

        // Select current monitor
        var monitorIndex = _profile.Capture?.MonitorIndex ?? 0;
        if (monitorIndex < MonitorComboBox.Items.Count)
        {
            MonitorComboBox.SelectedIndex = monitorIndex;
        }
        else if (MonitorComboBox.Items.Count > 0)
        {
            MonitorComboBox.SelectedIndex = 0;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Update display name
        _profile.DisplayName = DisplayNameTextBox.Text;

        // Update model file
        if (ModelFileComboBox.SelectedItem != null)
        {
            _profile.ModelFile = ModelFileComboBox.SelectedItem.ToString()!;
        }

        // Update monitor index
        _profile.Capture ??= new CaptureSettings();
        _profile.Capture.MonitorIndex = MonitorComboBox.SelectedIndex;

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
