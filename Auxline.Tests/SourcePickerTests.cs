using Auxline.Models;
using Auxline.Services;

namespace Auxline.Tests;

public sealed class SourcePickerTests
{
    private const string Spotify = "Spotify.exe";
    private const string Zen = "4155BD7E5BB5DDC";
    private const string Chrome = "Chrome";

    private static readonly SourceEntry[] SpotifyThenZen = [new(Spotify), new(Zen)];

    [Fact]
    public void BothPlaying_HigherPriorityWins()
    {
        SessionInfo[] sessions = [new(Zen, IsPlaying: true), new(Spotify, IsPlaying: true)];

        Assert.Equal(1, SourcePicker.Pick(sessions, Zen, SpotifyThenZen));
    }

    [Fact]
    public void OnlyLowerPriorityPlaying_ItWins()
    {
        SessionInfo[] sessions = [new(Spotify, IsPlaying: false), new(Zen, IsPlaying: true)];

        Assert.Equal(1, SourcePicker.Pick(sessions, Spotify, SpotifyThenZen));
    }

    [Fact]
    public void DisabledSource_NeverShownEvenWhenPlaying()
    {
        SessionInfo[] sessions = [new(Zen, IsPlaying: true)];
        SourceEntry[] sources = [new(Spotify), new(Zen, IsEnabled: false)];

        Assert.Null(SourcePicker.Pick(sessions, Zen, sources));
    }

    [Fact]
    public void NothingPlaying_PrefersWindowsCurrentSession()
    {
        SessionInfo[] sessions = [new(Spotify, IsPlaying: false), new(Zen, IsPlaying: false)];

        Assert.Equal(1, SourcePicker.Pick(sessions, Zen, SpotifyThenZen));
    }

    [Fact]
    public void NothingPlayingAndCurrentDisabled_FallsBackToHighestEnabled()
    {
        SessionInfo[] sessions = [new(Zen, IsPlaying: false), new(Spotify, IsPlaying: false)];
        SourceEntry[] sources = [new(Zen, IsEnabled: false), new(Spotify)];

        Assert.Equal(1, SourcePicker.Pick(sessions, Zen, sources));
    }

    [Fact]
    public void UnknownSource_IsEnabledAndRanksAfterListedOnes()
    {
        SessionInfo[] sessions = [new(Chrome, IsPlaying: true), new(Zen, IsPlaying: true)];

        Assert.Equal(1, SourcePicker.Pick(sessions, Chrome, SpotifyThenZen));
        Assert.Equal(0, SourcePicker.Pick([new(Chrome, IsPlaying: true)], null, SpotifyThenZen));
    }

    [Fact]
    public void AppIdsMatchCaseInsensitively()
    {
        SessionInfo[] sessions = [new("spotify.EXE", IsPlaying: true)];

        Assert.Null(SourcePicker.Pick(sessions, null, [new(Spotify, IsEnabled: false)]));
    }

    [Fact]
    public void NoSessions_ShowsNothing()
    {
        Assert.Null(SourcePicker.Pick([], null, SpotifyThenZen));
    }

    [Fact]
    public void Normalize_RemovesBlankAndDuplicateSources()
    {
        var settings = new AppSettings
        {
            Sources = [new(Spotify), new(" "), new("spotify.exe", IsEnabled: false), new(Zen)],
        }.Normalize();

        Assert.Equal([new SourceEntry(Spotify), new SourceEntry(Zen)], settings.Sources);
    }

    [Fact]
    public void Normalize_UnchangedSources_KeepsSameList()
    {
        var settings = new AppSettings();

        Assert.Same(settings.Sources, settings.Normalize().Sources);
    }
}
