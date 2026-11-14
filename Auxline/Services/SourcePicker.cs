using Auxline.Models;

namespace Auxline.Services;

/// <summary>A media session as seen by the picker: which app it belongs to and whether it's playing.</summary>
public readonly record struct SessionInfo(string AppId, bool IsPlaying);

/// <summary>Decides which media session the widget shows, based on the user's source list.</summary>
public static class SourcePicker
{
    /// <summary>
    /// 1. The playing, enabled session ranked highest in <paramref name="sources"/>.
    /// 2. Otherwise Windows' current session, if its source is enabled.
    /// 3. Otherwise the highest-ranked enabled session (e.g. paused).
    /// Sources not in the list yet count as enabled and rank after all listed ones.
    /// </summary>
    /// <returns>Index into <paramref name="sessions"/>, or null to show nothing.</returns>
    public static int? Pick(IReadOnlyList<SessionInfo> sessions, string? currentAppId, IReadOnlyList<SourceEntry> sources)
    {
        var enabled = Enumerable.Range(0, sessions.Count)
            .Where(i => IsEnabled(sessions[i].AppId, sources))
            .OrderBy(i => Rank(sessions[i].AppId, sources))
            .ToList();

        foreach (var i in enabled)
        {
            if (sessions[i].IsPlaying) return i;
        }

        foreach (var i in enabled)
        {
            if (SameApp(sessions[i].AppId, currentAppId)) return i;
        }

        return enabled.Count > 0 ? enabled[0] : null;
    }

    private static bool IsEnabled(string appId, IReadOnlyList<SourceEntry> sources) =>
        sources.FirstOrDefault(source => SameApp(source.AppId, appId))?.IsEnabled ?? true;

    /// <summary>Position in the list; unknown apps after all listed ones (OrderBy is stable, so they keep session order).</summary>
    private static int Rank(string appId, IReadOnlyList<SourceEntry> sources)
    {
        for (var i = 0; i < sources.Count; i++)
        {
            if (SameApp(sources[i].AppId, appId)) return i;
        }
        return int.MaxValue;
    }

    private static bool SameApp(string? first, string? second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
}
