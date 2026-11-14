using System.Text.RegularExpressions;

namespace Auxline.Models;

/// <summary>User settings, persisted as JSON. Immutable: changes produce a new instance via <c>with</c>.</summary>
public sealed record AppSettings
{
    public LayoutSettings Layout { get; init; } = new();

    public LookSettings Look { get; init; } = new();

    /// <summary>
    /// Media sources in priority order (first = preferred). New apps are appended when first seen.
    /// Spotify leads by default, matching the original "prefer Spotify" behaviour.
    /// </summary>
    public IReadOnlyList<SourceEntry> Sources { get; init; } = [new SourceEntry("Spotify.exe")];

    /// <summary>Brings every value into its valid range (hand-edited or outdated files may contain anything).</summary>
    public AppSettings Normalize() => this with
    {
        Layout = (Layout ?? new LayoutSettings()).Normalize(),
        Look = (Look ?? new LookSettings()).Normalize(),
        Sources = NormalizeSources(Sources),
    };

    /// <summary>
    /// Drops blank IDs and duplicates (first wins). Returns the same list when nothing changes, so "did the
    /// sources change?" can be a cheap reference check (settings change on every slider tick).
    /// </summary>
    private static IReadOnlyList<SourceEntry> NormalizeSources(IReadOnlyList<SourceEntry>? sources)
    {
        if (sources is null) return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = sources.Where(source => source is not null && !string.IsNullOrWhiteSpace(source.AppId) && seen.Add(source.AppId)).ToList();
        return cleaned.Count == sources.Count ? sources : cleaned;
    }
}

/// <param name="AppId">The GSMTC app ID (e.g. "Spotify.exe", "Chrome", or a hash for Firefox-based browsers).</param>
public sealed record SourceEntry(string AppId, bool IsEnabled = true);

/// <summary>Widget position and size, in DIPs.</summary>
public sealed record LayoutSettings
{
    public const double MinLeftOffset = 0;
    public const double MaxLeftOffset = 1000;
    public const double MinWidgetWidth = 120;
    public const double MaxWidgetWidthLimit = 600;
    public const double MinCoverSize = 16;
    public const double MaxCoverSize = 48;

    /// <summary>Gap between the taskbar's left edge and the widget (e.g. to clear the Widgets button).</summary>
    public double LeftOffset { get; init; } = 8;

    /// <summary>The widget shrinks to its content up to this width; longer titles are trimmed.</summary>
    public double MaxWidgetWidth { get; init; } = 240;

    public double CoverSize { get; init; } = 32;

    /// <summary>Left of the taskbar icons (default), or right of them next to the tray.</summary>
    public WidgetAnchor Anchor { get; init; } = WidgetAnchor.LeftOfIcons;

    /// <summary>
    /// Which monitor's taskbar, e.g. "\\.\DISPLAY1"; null = the primary one (follows Windows). An unplugged monitor
    /// falls back to the primary taskbar until it's back.
    /// </summary>
    public string? MonitorDevice { get; init; }

    public LayoutSettings Normalize() => this with
    {
        Anchor = Enum.IsDefined(Anchor) ? Anchor : WidgetAnchor.LeftOfIcons,
        MonitorDevice = string.IsNullOrWhiteSpace(MonitorDevice) ? null : MonitorDevice.Trim(),
        LeftOffset = SettingsMath.ClampOrDefault(LeftOffset, MinLeftOffset, MaxLeftOffset, 8),
        MaxWidgetWidth = SettingsMath.ClampOrDefault(MaxWidgetWidth, MinWidgetWidth, MaxWidgetWidthLimit, 240),
        CoverSize = SettingsMath.ClampOrDefault(CoverSize, MinCoverSize, MaxCoverSize, 32),
    };
}

/// <summary>On vertical taskbars: above the icons / below them, next to the tray.</summary>
public enum WidgetAnchor
{
    LeftOfIcons,
    RightOfIcons,
}

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

/// <summary>Flyout layouts (designs on the "Auxline – flyout styles" canvas).</summary>
public enum FlyoutStyle
{
    /// <summary>Cover left, three text lines, progress, controls, volume.</summary>
    Classic,

    /// <summary>Like Windows' own media flyout: app name, cover right, accent-coloured play button.</summary>
    Native,

    /// <summary>Full-width cover, centred title, big play button.</summary>
    BigCover,

    /// <summary>Background and accent taken from the album art.</summary>
    CoverTinted,

    /// <summary>One row: cover, title, play/pause, next, volume; progress line along the bottom.</summary>
    Compact,

    /// <summary>Cover, title, one line (progress, or volume via the speaker icon / mouse wheel), three icons.</summary>
    Minimalist,
}

public enum CornerStyle
{
    Round,
    Small,
    Square,
}

/// <summary>Fonts and colours. The widget's text follows the taskbar (system theme) unless a custom colour is set.</summary>
public sealed partial record LookSettings
{
    public const string DefaultFontFamily = "Segoe UI Variable Text";
    public const double MinFontSize = 8;
    public const double MaxFontSize = 24;

    /// <summary>Flyout and settings window. Not the widget: it sits on the taskbar, which always uses the system theme.</summary>
    public ThemePreference Theme { get; init; } = ThemePreference.System;

    public string FontFamily { get; init; } = DefaultFontFamily;

    public double TitleFontSize { get; init; } = 12;

    public double ArtistFontSize { get; init; } = 11;

    /// <summary>Widget text as #RRGGBB or #AARRGGBB; null = automatic (contrasts with the taskbar).</summary>
    public string? TextColor { get; init; }

    public FlyoutStyle FlyoutStyle { get; init; } = FlyoutStyle.Classic;

    public CornerStyle FlyoutCorners { get; init; } = CornerStyle.Round;

    public bool UseAcrylic { get; init; } = true;

    public LookSettings Normalize() => this with
    {
        Theme = Enum.IsDefined(Theme) ? Theme : ThemePreference.System,
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? DefaultFontFamily : FontFamily.Trim(),
        TitleFontSize = SettingsMath.ClampOrDefault(TitleFontSize, MinFontSize, MaxFontSize, 12),
        ArtistFontSize = SettingsMath.ClampOrDefault(ArtistFontSize, MinFontSize, MaxFontSize, 11),
        TextColor = IsHexColor(TextColor) ? TextColor!.ToUpperInvariant() : null,
        FlyoutStyle = Enum.IsDefined(FlyoutStyle) ? FlyoutStyle : FlyoutStyle.Classic,
        FlyoutCorners = Enum.IsDefined(FlyoutCorners) ? FlyoutCorners : CornerStyle.Round,
    };

    public static bool IsHexColor(string? value) => value is not null && HexColorPattern().IsMatch(value);

    [GeneratedRegex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexColorPattern();
}

internal static class SettingsMath
{
    /// <summary>NaN/Infinity can't be clamped meaningfully, so they fall back to the default.</summary>
    public static double ClampOrDefault(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
