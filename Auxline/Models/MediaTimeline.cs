namespace Auxline.Models;

/// <param name="Position">Position at <paramref name="LastUpdated"/>, relative to the track start.</param>
public sealed record MediaTimeline(
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset LastUpdated,
    double PlaybackRate,
    bool CanSeek)
{
    /// <summary>
    /// Players report the position only when it changes discontinuously (Spotify: seek, pause, track change),
    /// so extrapolate from the last report while playing.
    /// </summary>
    public TimeSpan EstimatePosition(DateTimeOffset now, bool isPlaying)
    {
        var isTimestampUsable = LastUpdated != default && LastUpdated <= now;
        var elapsed = isPlaying && isTimestampUsable ? now - LastUpdated : TimeSpan.Zero;
        return Clamp(Position + elapsed * PlaybackRate);
    }

    private TimeSpan Clamp(TimeSpan value) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value > Duration ? Duration : value;
}
