using Auxline.Models;

namespace Auxline.Tests;

public sealed class MediaTimelineTests
{
    private static readonly DateTimeOffset ReportedAt = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static MediaTimeline Timeline(double positionSeconds, double rate = 1, DateTimeOffset? lastUpdated = null) => new(
        Position: TimeSpan.FromSeconds(positionSeconds),
        Duration: TimeSpan.FromSeconds(180),
        LastUpdated: lastUpdated ?? ReportedAt,
        PlaybackRate: rate,
        CanSeek: true);

    [Fact]
    public void Paused_ReturnsReportedPosition()
    {
        var position = Timeline(60).EstimatePosition(ReportedAt.AddSeconds(10), isPlaying: false);

        Assert.Equal(TimeSpan.FromSeconds(60), position);
    }

    [Fact]
    public void Playing_AddsElapsedTimeScaledByRate()
    {
        var position = Timeline(60, rate: 1.5).EstimatePosition(ReportedAt.AddSeconds(10), isPlaying: true);

        Assert.Equal(TimeSpan.FromSeconds(75), position);
    }

    [Fact]
    public void Playing_ClampsToDuration()
    {
        var position = Timeline(170).EstimatePosition(ReportedAt.AddMinutes(5), isPlaying: true);

        Assert.Equal(TimeSpan.FromSeconds(180), position);
    }

    [Fact]
    public void TimestampInTheFuture_DoesNotExtrapolate()
    {
        var position = Timeline(60, lastUpdated: ReportedAt.AddSeconds(30)).EstimatePosition(ReportedAt, isPlaying: true);

        Assert.Equal(TimeSpan.FromSeconds(60), position);
    }

    [Fact]
    public void MissingTimestamp_DoesNotExtrapolate()
    {
        var position = Timeline(60, lastUpdated: default(DateTimeOffset)).EstimatePosition(ReportedAt, isPlaying: true);

        Assert.Equal(TimeSpan.FromSeconds(60), position);
    }
}
