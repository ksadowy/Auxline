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
    private readonly SessionManager _manager;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile Session? _session;

    /// <summary>User's source list (priority + on/off); replaced as a whole, read during refreshes.</summary>
    private volatile IReadOnlyList<SourceEntry> _sources = new AppSettings().Sources;

    /// <summary>Raised on a background thread. Null means nothing is playing.</summary>
    public event EventHandler<MediaTrack?>? TrackChanged;

    /// <summary>
    /// Raised on a background thread when only the position/duration changed (seek, track progress report).
    /// Kept separate from <see cref="TrackChanged"/> so it doesn't re-read cover art. Null means no timeline.
    /// </summary>
    public event EventHandler<MediaTimeline?>? TimelineChanged;

    /// <summary>Raised on a background thread with the app IDs of all current sessions, so new sources can be listed.</summary>
    public event EventHandler<IReadOnlyList<string>>? SourcesSeen;

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

    /// <summary>Applies a changed source list and re-picks the session right away.</summary>
    public Task SetSourcesAsync(IReadOnlyList<SourceEntry> sources)
    {
        _sources = sources;
        return RefreshAsync();
    }

    public Task<bool> TogglePlayPauseAsync() => SendAsync(s => s.TryTogglePlayPauseAsync());

    public Task<bool> SkipNextAsync() => SendAsync(s => s.TrySkipNextAsync());

    public Task<bool> SkipPreviousAsync() => SendAsync(s => s.TrySkipPreviousAsync());

    /// <param name="position">Relative to the track start.</param>
    public Task<bool> SeekAsync(TimeSpan position) =>
        SendAsync(s => s.TryChangePlaybackPositionAsync((s.GetTimelineProperties().StartTime + position).Ticks));

    public void Dispose()
    {
        _manager.SessionsChanged -= OnSessionsChanged;
        _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        AttachTo(null);
    }

    /// <summary>Follows the user's source list; see <see cref="SourcePicker.Pick"/> for the rules.</summary>
    private Session? PickSession()
    {
        var sessions = _manager.GetSessions();
        var infos = sessions.Select(session => new SessionInfo(session.SourceAppUserModelId, IsPlaying(session))).ToList();
        SourcesSeen?.Invoke(this, infos.Select(info => info.AppId).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        var index = SourcePicker.Pick(infos, _manager.GetCurrentSession()?.SourceAppUserModelId, _sources);
        return index is { } i ? sessions[i] : null;
    }

    private static bool IsPlaying(Session session) =>
        session.GetPlaybackInfo().PlaybackStatus ==
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    private void AttachTo(Session? session)
    {
        if (ReferenceEquals(session, _session)) return;

        if (_session is { } old)
        {
            old.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            old.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            old.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }

        _session = session;

        if (session is not null)
        {
            session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
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
            Timeline: ReadTimeline(session),
            Thumbnail: await ReadThumbnailAsync(properties.Thumbnail));
    }

    /// <returns>Null for live streams or players that report no duration.</returns>
    private static MediaTimeline? ReadTimeline(Session session)
    {
        var timeline = session.GetTimelineProperties();
        var duration = timeline.EndTime - timeline.StartTime;
        if (duration <= TimeSpan.Zero) return null;

        var playback = session.GetPlaybackInfo();
        return new MediaTimeline(
            Position: timeline.Position - timeline.StartTime,
            Duration: duration,
            LastUpdated: timeline.LastUpdatedTime,
            PlaybackRate: playback.PlaybackRate ?? 1,
            CanSeek: playback.Controls.IsPlaybackPositionEnabled);
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

    private void OnTimelinePropertiesChanged(Session sender, TimelinePropertiesChangedEventArgs args) =>
        _ = PublishTimelineAsync(sender);

    /// <summary>
    /// Shares the refresh lock: a refresh in flight (e.g. still loading cover art) carries an older timeline,
    /// so this update must be published after it, not overwritten by it.
    /// </summary>
    private async Task PublishTimelineAsync(Session session)
    {
        await _refreshLock.WaitAsync();
        try
        {
            TimelineChanged?.Invoke(this, ReadTimeline(session));
        }
        catch (COMException ex)
        {
            // Session closed mid-read; SessionsChanged follows.
            Trace.TraceWarning($"Timeline read failed: {ex.Message}");
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
