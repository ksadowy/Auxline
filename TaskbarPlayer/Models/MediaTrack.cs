namespace TaskbarPlayer.Models;

public sealed record MediaTrack(
    string SourceAppId,
    string Title,
    string Artist,
    string Album,
    bool IsPlaying,
    byte[]? Thumbnail);
