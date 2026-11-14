using System.Diagnostics;
using System.Windows;
using Auxline.Diagnostics;
using Auxline.Models;
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
    private SettingsService? _settings;
    private AppTrayIcon? _tray;

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
        }
        // No explicit first refresh: applying the initial source list in StartUi already picks the session.
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _media?.Dispose();
        _volume?.Dispose();
        _taskbar?.Dispose();
        _settings?.Dispose(); // writes a change still waiting for its debounced save
        _singleInstance?.Dispose();
        AppDiagnostics.Stop();
        base.OnExit(e);
    }

    private void StartUi(MediaService media)
    {
        _volume = new VolumeService();
        _taskbar = new TaskbarService();
        _settings = SettingsService.Load(SettingsService.DefaultPath);
        var player = new PlayerViewModel(media, _volume, Dispatcher);

        var flyout = new FlyoutWindow { DataContext = player };
        var widget = new WidgetWindow(_taskbar) { DataContext = player };
        widget.Clicked += (_, _) => flyout.Toggle(widget.Bounds, widget.TaskbarEdge);

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the Auxline executable path.");
        _tray = new AppTrayIcon(new AutostartService(executablePath), _taskbar.UsesLightTheme());
        _tray.SettingsRequested += (_, _) => ShowSingle(CreateSettingsWindow);
        _tray.DebugWindowRequested += (_, _) => ShowSingle(() => new DebugWindow { DataContext = player });
        _tray.ExitRequested += (_, _) => Shutdown();

        IReadOnlyList<SourceEntry>? appliedSources = null;

        void ApplySettings(AppSettings settings)
        {
            // Same list instance = unchanged (see AppSettings.Normalize); re-picking reads cover art, so skip it
            // for the many changes that don't touch sources (e.g. every slider tick).
            if (!ReferenceEquals(settings.Sources, appliedSources))
            {
                appliedSources = settings.Sources;
                _ = media.SetSourcesAsync(settings.Sources);
            }

            // The widget and tray icon sit on the taskbar, which always follows the system theme.
            var isTaskbarLight = _taskbar.UsesLightTheme();
            var isLight = settings.Look.Theme switch
            {
                ThemePreference.Light => true,
                ThemePreference.Dark => false,
                _ => isTaskbarLight,
            };
            widget.ApplyLayout(settings.Layout);
            widget.ApplyLook(settings.Look, isTaskbarLight);
            flyout.ApplyLook(settings.Look, isLight);
            foreach (var window in Windows.OfType<SettingsWindow>()) window.ApplyTheme(settings.Look.Theme);
        }

        ApplySettings(_settings.Current);
        _settings.Changed += (_, settings) => ApplySettings(settings);
        media.SourcesSeen += (_, appIds) => Dispatcher.InvokeAsync(() => AddNewSources(appIds));
        _taskbar.ThemeChanged += (_, _) => Dispatcher.InvokeAsync(() =>
        {
            ApplySettings(_settings.Current);
            _tray.ApplyTheme(_taskbar.UsesLightTheme());
        });
        widget.Show();
    }

    /// <summary>Lists apps the first time they show up in Windows media controls: enabled, lowest priority.</summary>
    private void AddNewSources(IReadOnlyList<string> appIds)
    {
        var known = _settings!.Current.Sources;
        var added = appIds
            .Where(id => !known.Any(source => string.Equals(source.AppId, id, StringComparison.OrdinalIgnoreCase)))
            .Select(id => new SourceEntry(id))
            .ToList();
        if (added.Count == 0) return;

        _settings.Update(settings => settings with { Sources = [.. settings.Sources, .. added] });
    }

    private SettingsWindow CreateSettingsWindow()
    {
        var viewModel = new SettingsViewModel(_settings!);
        var window = new SettingsWindow { DataContext = viewModel };
        window.ApplyTheme(_settings!.Current.Look.Theme);
        window.Closed += (_, _) => viewModel.Dispose();
        return window;
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

    /// <summary>One window per kind: asking again brings the open one to the front instead of opening another.</summary>
    private void ShowSingle<TWindow>(Func<TWindow> create) where TWindow : Window
    {
        var window = Windows.OfType<TWindow>().FirstOrDefault() ?? create();
        window.Show();
        window.Activate();
    }
}
