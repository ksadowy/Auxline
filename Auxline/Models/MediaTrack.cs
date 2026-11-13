namespace Auxline.Models;

public sealed record MediaTrack(
    string SourceAppId,
    string Title,
    string Artist,
    string Album,
    bool IsPlaying,
    bool CanSkipNext,
    bool CanSkipPrevious,
    MediaTimeline? Timeline,
    byte[]? Thumbnail);
