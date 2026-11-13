using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskbarPlayer.Models;
using TaskbarPlayer.Services;

namespace TaskbarPlayer.ViewModels;

public sealed partial class DebugViewModel : ObservableObject
{
    private readonly MediaService _media;

    [ObservableProperty]
    private string _source = "";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _artist = "";

    [ObservableProperty]
    private string _album = "";

    [ObservableProperty]
    private string _playback = "";

    [ObservableProperty]
    private BitmapImage? _cover;

    [ObservableProperty]
    private string _status = "";

    public DebugViewModel(MediaService media, Dispatcher dispatcher)
    {
        _media = media;
        Show(null);
        _media.TrackChanged += (_, track) => dispatcher.InvokeAsync(() => Show(track));
    }

    [RelayCommand]
    private Task TogglePlayPause() => RunAsync(_media.TogglePlayPauseAsync, "Play/pause");

    [RelayCommand]
    private Task SkipNext() => RunAsync(_media.SkipNextAsync, "Next");

    [RelayCommand]
    private Task SkipPrevious() => RunAsync(_media.SkipPreviousAsync, "Previous");

    private async Task RunAsync(Func<Task<bool>> command, string commandName)
    {
        var isAccepted = await command();
        Status = isAccepted ? "" : $"{commandName} rejected by player (or no session).";
    }

    private void Show(MediaTrack? track)
    {
        Source = track?.SourceAppId ?? "-";
        Title = track?.Title ?? "Nothing playing";
        Artist = track?.Artist ?? "";
        Album = track?.Album ?? "";
        Playback = track is null ? "" : track.IsPlaying ? "Playing" : "Paused";
        Cover = track?.Thumbnail is { } bytes ? ToBitmap(bytes) : null;
    }

    private static BitmapImage? ToBitmap(byte[] bytes)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (NotSupportedException)
        {
            // Player sent cover data WPF cannot decode; show the track without it.
            return null;
        }
    }
}
