using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Auxline.Diagnostics;
using Auxline.Models;
using Auxline.Services;

namespace Auxline.ViewModels;

/// <summary>Settings window state. Every change is applied live and saved by <see cref="SettingsService"/>.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;

    /// <summary>True while values are copied in from the settings, so that doesn't echo back as an update.</summary>
    private bool _isLoading;

    [ObservableProperty]
    private double _leftOffset;

    [ObservableProperty]
    private double _maxWidgetWidth;

    [ObservableProperty]
    private double _coverSize;

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;
        Load(settings.Current);
    }

    partial void OnLeftOffsetChanged(double value) => UpdateLayout(layout => layout with { LeftOffset = value });

    partial void OnMaxWidgetWidthChanged(double value) => UpdateLayout(layout => layout with { MaxWidgetWidth = value });

    partial void OnCoverSizeChanged(double value) => UpdateLayout(layout => layout with { CoverSize = value });

    [RelayCommand]
    private void ResetToDefaults()
    {
        _settings.Update(_ => new AppSettings());
        Load(_settings.Current);
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppDiagnostics.LogDirectory);
            Process.Start(new ProcessStartInfo { FileName = AppDiagnostics.LogDirectory, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not open the log folder: {ex.Message}");
        }
    }

    private void UpdateLayout(Func<LayoutSettings, LayoutSettings> change)
    {
        if (_isLoading) return;
        _settings.Update(settings => settings with { Layout = change(settings.Layout) });
    }

    private void Load(AppSettings settings)
    {
        _isLoading = true;
        LeftOffset = settings.Layout.LeftOffset;
        MaxWidgetWidth = settings.Layout.MaxWidgetWidth;
        CoverSize = settings.Layout.CoverSize;
        _isLoading = false;
    }
}
