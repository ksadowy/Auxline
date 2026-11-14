using System.IO;
using System.Windows.Media.Imaging;
using Auxline.Controls;
using Color = System.Windows.Media.Color;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Auxline.Models;
using Auxline.Services;

namespace Auxline.ViewModels;

/// <summary>Now-playing state + commands, shared by the widget, flyout and debug windows.</summary>
public sealed partial class PlayerViewModel : ObservableObject
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>20 readings a second: smooth enough for the meter, cheap enough to run all the time music plays.</summary>
    private static readonly TimeSpan MeterInterval = TimeSpan.FromMilliseconds(50);

    private readonly MediaService _media;
    private readonly VolumeService _volumeService;
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _meterTimer;
    private string? _processName;
    private MediaTimeline? _timeline;
    private bool _isProgressShown;
    private bool _isMeterShown;

    [ObservableProperty]
    private bool _hasTrack;

    [ObservableProperty]
    private string _source = "";

    /// <summary>Readable name of the playing app (e.g. "Spotify", "Zen"), for the native flyout style's header.</summary>
    [ObservableProperty]
    private string _sourceName = "";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _artist = "";

    [ObservableProperty]
    private string _album = "";

    [ObservableProperty]
    private string _playback = "";

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SkipNextCommand))]
    private bool _canSkipNext;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SkipPreviousCommand))]
    private bool _canSkipPrevious;

    [ObservableProperty]
    private BitmapImage? _cover;

    /// <summary>Dominant colour of the cover (null without one), for the cover-tinted flyout style.</summary>
    [ObservableProperty]
    private Color? _coverColor;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private double _volumePercent;

    [ObservableProperty]
    private bool _hasAudioSession;

    [ObservableProperty]
    private string _volumeStatus = "";

    /// <summary>How loud the playing app is right now, 0..1, for the widget's music meter (0 while it's not shown).</summary>
    [ObservableProperty]
    private double _audioLevel;

    [ObservableProperty]
    private bool _hasTimeline;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SeekCommand))]
    private bool _canSeek;

    [ObservableProperty]
    private double _positionSeconds;

    [ObservableProperty]
    private double _durationSeconds;

    [ObservableProperty]
    private string _positionText = "";

    [ObservableProperty]
    private string _durationText = "";

    /// <summary>True while the user presses/drags the progress slider, so timer updates don't fight the thumb.</summary>
    public bool IsSeeking { get; set; }

    public PlayerViewModel(MediaService media, VolumeService volumeService, Dispatcher dispatcher)
    {
        _media = media;
        _volumeService = volumeService;
        _progressTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = ProgressInterval };
        _progressTimer.Tick += (_, _) => ShowPosition();
        _meterTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = MeterInterval };
        _meterTimer.Tick += (_, _) => ShowAudioLevel();

        Show(null);
        _media.TrackChanged += (_, track) => dispatcher.InvokeAsync(() => Show(track));
        _media.TimelineChanged += (_, timeline) => dispatcher.InvokeAsync(() => ShowTimeline(timeline));
    }

    /// <summary>The progress bar only ticks while a view shows it, so a hidden flyout costs no CPU.</summary>
    public void SetProgressShown(bool isShown)
    {
        _isProgressShown = isShown;
        IsSeeking = false;
        ShowPosition();
        UpdateProgressTimer();
    }

    /// <summary>The level is only read while the meter is visible and music plays, so a hidden or paused one costs no CPU.</summary>
    public void SetMeterShown(bool isShown)
    {
        _isMeterShown = isShown;
        UpdateMeterTimer();
    }

    [RelayCommand]
    private Task TogglePlayPause() => RunAsync(_media.TogglePlayPauseAsync, "Play/pause");

    [RelayCommand(CanExecute = nameof(CanSkipNext))]
    private Task SkipNext() => RunAsync(_media.SkipNextAsync, "Next");

    [RelayCommand(CanExecute = nameof(CanSkipPrevious))]
    private Task SkipPrevious() => RunAsync(_media.SkipPreviousAsync, "Previous");

    [RelayCommand(CanExecute = nameof(CanSeek))]
    private async Task Seek()
    {
        var target = TimeSpan.FromSeconds(PositionSeconds);
        var isAccepted = await RunAsync(() => _media.SeekAsync(target), "Seek");

        // The player confirms via TimelineChanged later; until then extrapolate from the requested position.
        if (isAccepted && _timeline is not null)
        {
            _timeline = _timeline with { Position = target, LastUpdated = DateTimeOffset.Now };
        }
    }

    private async Task<bool> RunAsync(Func<Task<bool>> command, string commandName)
    {
        var isAccepted = await command();
        Status = isAccepted ? "" : $"{commandName} rejected by player (or no session).";
        return isAccepted;
    }

    private void Show(MediaTrack? track)
    {
        HasTrack = track is not null;
        Source = track?.SourceAppId ?? "-";
        SourceName = track is null ? "" : AppIdentity.ToDisplayName(track.SourceAppId);
        Title = track?.Title ?? "Nothing playing";
        Artist = track?.Artist ?? "";
        Album = track?.Album ?? "";
        Playback = track is null ? "" : track.IsPlaying ? "Playing" : "Paused";
        IsPlaying = track?.IsPlaying ?? false;
        CanSkipNext = track?.CanSkipNext ?? false;
        CanSkipPrevious = track?.CanSkipPrevious ?? false;
        Cover = track?.Thumbnail is { } bytes ? ToBitmap(bytes) : null;
        CoverColor = Cover is null ? null : CoverPalette.DominantColor(Cover);
        _processName = track is null ? null : AppIdentity.ToProcessName(track.SourceAppId);
        ShowVolume();
        ShowTimeline(track?.Timeline);
        UpdateMeterTimer();
    }

    private void ShowTimeline(MediaTimeline? timeline)
    {
        _timeline = timeline;
        HasTimeline = timeline is not null;
        CanSeek = timeline?.CanSeek ?? false;
        DurationSeconds = timeline?.Duration.TotalSeconds ?? 0;
        DurationText = timeline is null ? "" : FormatTime(timeline.Duration);
        ShowPosition();
        UpdateProgressTimer();
    }

    private void ShowPosition()
    {
        if (IsSeeking || _timeline is null) return;
        PositionSeconds = _timeline.EstimatePosition(DateTimeOffset.Now, IsPlaying).TotalSeconds;
    }

    /// <summary>Also follows the thumb while the user drags it.</summary>
    partial void OnPositionSecondsChanged(double value) => PositionText = FormatTime(TimeSpan.FromSeconds(value));

    private void UpdateProgressTimer() =>
        _progressTimer.IsEnabled = _isProgressShown && IsPlaying && _timeline is not null;

    private static string FormatTime(TimeSpan time) =>
        time.ToString(time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");

    private void UpdateMeterTimer()
    {
        _meterTimer.IsEnabled = _isMeterShown && IsPlaying && _processName is not null;
        if (!_meterTimer.IsEnabled) AudioLevel = 0;
    }

    private void ShowAudioLevel()
    {
        if (_processName is null) return;
        AudioLevel = MeterBallistics.Next(AudioLevel, _volumeService.GetPeak(_processName));
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
