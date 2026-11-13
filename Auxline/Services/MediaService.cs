using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Auxline.Models;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using SessionManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace Auxline.Services;

/// <summary>
/// Tracks the most relevant media session via GSMTC and exposes playback controls.
/// Event-driven: GSMTC events trigger a refresh, no polling.
/// </summary>
public sealed class MediaService : IDisposable
{
    private const string PreferredAppId = "Spotify";

    private readonly SessionManager _manager;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile Session? _session;

    /// <summary>Raised on a background thread. Null means nothing is playing.</summary>
    public event EventHandler<MediaTrack?>? TrackChanged;

    private MediaService(SessionManager manager)
    {
        _manager = manager;
        _manager.SessionsChanged += OnSessionsChanged;
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
    }

    public static async Task<MediaService> CreateAsync() =>
        new(await SessionManager.RequestAsync());

    public async Task RefreshAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            AttachTo(PickSession());
            var track = _session is { } session ? await ReadTrackAsync(session) : null;
            TrackChanged?.Invoke(this, track);
        }
        catch (COMException ex)
        {
            // Session closed mid-read. SessionsChanged follows and triggers a fresh refresh.
            Trace.TraceWarning($"Media refresh failed: {ex.Message}");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public Task<bool> TogglePlayPauseAsync() => SendAsync(s => s.TryTogglePlayPauseAsync());

    public Task<bool> SkipNextAsync() => SendAsync(s => s.TrySkipNextAsync());

    public Task<bool> SkipPreviousAsync() => SendAsync(s => s.TrySkipPreviousAsync());

    public void Dispose()
    {
        _manager.SessionsChanged -= OnSessionsChanged;
        _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        AttachTo(null);
    }

    /// <summary>Playing Spotify, then any playing session, then Windows' current session.</summary>
    private Session? PickSession()
    {
        var playing = _manager.GetSessions().Where(IsPlaying).ToList();
        return playing.FirstOrDefault(IsPreferredApp)
            ?? playing.FirstOrDefault()
            ?? _manager.GetCurrentSession();
    }

    private static bool IsPlaying(Session session) =>
        session.GetPlaybackInfo().PlaybackStatus ==
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    private static bool IsPreferredApp(Session session) =>
        session.SourceAppUserModelId.Contains(PreferredAppId, StringComparison.OrdinalIgnoreCase);

    private void AttachTo(Session? session)
    {
        if (ReferenceEquals(session, _session)) return;

        if (_session is { } old)
        {
            old.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            old.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _session = session;

        if (session is not null)
        {
            session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
    }

    private static async Task<MediaTrack> ReadTrackAsync(Session session)
    {
        var properties = await session.TryGetMediaPropertiesAsync();
        var controls = session.GetPlaybackInfo().Controls;
        return new MediaTrack(
            SourceAppId: session.SourceAppUserModelId,
            Title: properties.Title,
            Artist: properties.Artist,
            Album: properties.AlbumTitle,
            IsPlaying: IsPlaying(session),
            CanSkipNext: controls.IsNextEnabled,
            CanSkipPrevious: controls.IsPreviousEnabled,
            Thumbnail: await ReadThumbnailAsync(properties.Thumbnail));
    }

    /// <summary>Cover art is optional: on failure the track is shown without it.</summary>
    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null) return null;
        try
        {
            using var winRtStream = await thumbnail.OpenReadAsync();
            using var stream = winRtStream.AsStreamForRead();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is COMException or IOException)
        {
            Trace.TraceWarning($"Thumbnail read failed: {ex.Message}");
            return null;
        }
    }

    private async Task<bool> SendAsync(Func<Session, IAsyncOperation<bool>> command)
    {
        if (_session is not { } session) return false;
        try
        {
            return await command(session);
        }
        catch (COMException ex)
        {
            Trace.TraceWarning($"Media command failed: {ex.Message}");
            return false;
        }
    }

    private void OnSessionsChanged(SessionManager sender, SessionsChangedEventArgs args) => _ = RefreshAsync();

    private void OnCurrentSessionChanged(SessionManager sender, CurrentSessionChangedEventArgs args) => _ = RefreshAsync();

    private void OnMediaPropertiesChanged(Session sender, MediaPropertiesChangedEventArgs args) => _ = RefreshAsync();

    private void OnPlaybackInfoChanged(Session sender, PlaybackInfoChangedEventArgs args) => _ = RefreshAsync();
}
