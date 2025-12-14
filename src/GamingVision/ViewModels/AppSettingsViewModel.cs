using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GamingVision.Models;
using GamingVision.Utilities;

namespace GamingVision.ViewModels;

/// <summary>
/// ViewModel for the application settings window.
/// </summary>
public partial class AppSettingsViewModel : ObservableObject
{
    private readonly AppConfiguration _config;
    private readonly ConfigManager _configManager;

    [ObservableProperty]
    private bool _useDirectML;

    [ObservableProperty]
    private bool _highContrast;

    [ObservableProperty]
    private bool _largeText;

    [ObservableProperty]
    private string _gpuName = "Detecting...";

    public AppSettingsViewModel(AppConfiguration config, ConfigManager configManager)
    {
        _config = config;
        _configManager = configManager;

        LoadSettings();
        DetectGpu();
    }

    private void LoadSettings()
    {
        UseDirectML = _config.Application.UseDirectML;
        HighContrast = _config.Application.Accessibility.HighContrast;
        LargeText = _config.Application.Accessibility.LargeText;
    }

    private void DetectGpu()
    {
        Task.Run(() =>
        {
            var gpu = GpuDetector.GetPrimaryGpu();
            var name = gpu?.DisplayString ?? "No GPU detected";

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                GpuName = name;
            });
        });
    }

    private void SaveSettings()
    {
        _config.Application.UseDirectML = UseDirectML;
        _config.Application.Accessibility.HighContrast = HighContrast;
        _config.Application.Accessibility.LargeText = LargeText;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        SaveSettings();
        await _configManager.SaveAsync(_config);
    }

    /// <summary>
    /// Saves settings and returns true to indicate dialog should close.
    /// </summary>
    public async Task<bool> SaveAndCloseAsync()
    {
        await SaveAsync();
        return true;
    }
}
