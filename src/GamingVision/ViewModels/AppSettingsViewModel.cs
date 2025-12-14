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
    private readonly AppConfiguration _appConfig;
    private readonly ConfigManager _configManager;

    [ObservableProperty]
    private bool _useDirectML;

    [ObservableProperty]
    private string _gpuName = "Detecting...";

    public AppSettingsViewModel(AppConfiguration appConfig, ConfigManager configManager)
    {
        _appConfig = appConfig;
        _configManager = configManager;

        LoadSettings();
        DetectGpu();
    }

    private void LoadSettings()
    {
        UseDirectML = _appConfig.UseDirectML;
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
        _appConfig.UseDirectML = UseDirectML;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        SaveSettings();
        await _configManager.SaveAppSettingsAsync(_appConfig);
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
