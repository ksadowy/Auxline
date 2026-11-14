using System.Diagnostics;
using System.Windows;
using Auxline.Diagnostics;
using Auxline.Services;
using Auxline.ViewModels;
using Auxline.Views;

namespace Auxline;

public partial class App : Application
{
    /// <summary>Per user session: a second launch exits instead of drawing a second widget.</summary>
    private const string SingleInstanceMutexName = @"Local\Auxline.SingleInstance";

    private Mutex? _singleInstance;
    private MediaService? _media;
    private VolumeService? _volume;
    private TaskbarService? _taskbar;
    private AppTrayIcon? _tray;
    private DebugWindow? _debugWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        // After the single-instance check, so a second launch doesn't write to the running instance's log.
        AppDiagnostics.Start(this);

        // Runs in the background; only "Exit" in the tray menu ends the app.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _media = await MediaService.CreateAsync();
        }
        catch (Exception ex)
        {
            ReportStartupFailure("Could not access Windows media controls.", ex);
            return;
        }

        try
        {
            StartUi(_media);
        }
        catch (Exception ex)
        {
            // A half-started app (e.g. widget but no tray icon) would have no way to exit, so fail visibly.
            ReportStartupFailure("Auxline could not start.", ex);
            return;
        }

        await _media.RefreshAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _media?.Dispose();
        _volume?.Dispose();
        _taskbar?.Dispose();
        _singleInstance?.Dispose();
        AppDiagnostics.Stop();
        base.OnExit(e);
    }

    private void StartUi(MediaService media)
    {
        _volume = new VolumeService();
        _taskbar = new TaskbarService();
        var player = new PlayerViewModel(media, _volume, Dispatcher);

        var flyout = new FlyoutWindow(_taskbar) { DataContext = player };
        var widget = new WidgetWindow(_taskbar) { DataContext = player };
        widget.Clicked += (_, _) => flyout.Toggle(widget.Bounds);
        widget.Show();

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the Auxline executable path.");
        _tray = new AppTrayIcon(new AutostartService(executablePath), _taskbar.UsesLightTheme());
        _tray.DebugWindowRequested += (_, _) => ShowDebugWindow(player);
        _tray.ExitRequested += (_, _) => Shutdown();
    }

    private void ReportStartupFailure(string summary, Exception ex)
    {
        Trace.TraceError($"{summary} {ex}");
        MessageBox.Show(
            $"{summary}\n\n{ex.Message}\n\nDetails are in the log: {AppDiagnostics.LogDirectory}",
            "Auxline",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
    }

    private void ShowDebugWindow(PlayerViewModel player)
    {
        if (_debugWindow is null)
        {
            _debugWindow = new DebugWindow { DataContext = player };
            _debugWindow.Closed += (_, _) => _debugWindow = null;
        }
        _debugWindow.Show();
        _debugWindow.Activate();
    }
}
