using System.Windows;
using TaskbarPlayer.Services;
using TaskbarPlayer.ViewModels;
using TaskbarPlayer.Views;

namespace TaskbarPlayer;

public partial class App : Application
{
    private MediaService? _media;
    private VolumeService? _volume;

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
                "Taskbar Player",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _volume = new VolumeService();
        new DebugWindow { DataContext = new DebugViewModel(_media, _volume, Dispatcher) }.Show();
        await _media.RefreshAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _media?.Dispose();
        _volume?.Dispose();
        base.OnExit(e);
    }
}
