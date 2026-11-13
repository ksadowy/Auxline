using System.Windows;
using Auxline.Services;
using Auxline.ViewModels;
using Auxline.Views;

namespace Auxline;

public partial class App : Application
{
    private MediaService? _media;
    private VolumeService? _volume;
    private TaskbarService? _taskbar;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _media = await MediaService.CreateAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not access Windows media controls.\n\n{ex.Message}",
                "Auxline",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _volume = new VolumeService();
        _taskbar = new TaskbarService();
        var player = new PlayerViewModel(_media, _volume, Dispatcher);

        // Closing the debug window exits the app until the tray icon (Phase 5) exists.
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow = new DebugWindow { DataContext = player };
        MainWindow.Show();
        new WidgetWindow(_taskbar) { DataContext = player }.Show();

        await _media.RefreshAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _media?.Dispose();
        _volume?.Dispose();
        _taskbar?.Dispose();
        base.OnExit(e);
    }
}
