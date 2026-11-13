using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Auxline.Models;
using Auxline.Services;

namespace Auxline.ViewModels;

/// <summary>Now-playing state + commands, shared by the widget and debug windows.</summary>
public sealed partial class PlayerViewModel : ObservableObject
{
    private readonly MediaService _media;
    private readonly VolumeService _volumeService;
    private string? _processName;

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

    [ObservableProperty]
    private double _volumePercent;

    [ObservableProperty]
    private bool _hasAudioSession;

    [ObservableProperty]
    private string _volumeStatus = "";

    public PlayerViewModel(MediaService media, VolumeService volumeService, Dispatcher dispatcher)
    {
        _media = media;
        _volumeService = volumeService;
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
        _processName = track is null ? null : AppIdentity.ToProcessName(track.SourceAppId);
        ShowVolume();
    }

    /// <summary>Re-read on every track/playback event, which also picks up changes made in the Windows mixer.</summary>
    private void ShowVolume()
    {
        var volume = _processName is null ? null : _volumeService.GetVolume(_processName);
        HasAudioSession = volume is not null;
        VolumePercent = (volume ?? 0) * 100;
        VolumeStatus = _processName is null || HasAudioSession
            ? ""
            : $"No audio session for process '{_processName}'.";
    }

    partial void OnVolumePercentChanged(double value)
    {
        if (!HasAudioSession || _processName is null) return;
        _volumeService.SetVolume(_processName, (float)(value / 100));
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
