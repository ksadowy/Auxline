using System.Windows;
using TaskbarPlayer.Services;
using TaskbarPlayer.ViewModels;
using TaskbarPlayer.Views;

namespace TaskbarPlayer;

public partial class App : Application
{
    private MediaService? _media;

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

        new DebugWindow { DataContext = new DebugViewModel(_media, Dispatcher) }.Show();
        await _media.RefreshAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _media?.Dispose();
        base.OnExit(e);
    }
}
