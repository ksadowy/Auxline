using System.Text.RegularExpressions;

namespace Auxline.Models;

/// <summary>User settings, persisted as JSON. Immutable: changes produce a new instance via <c>with</c>.</summary>
public sealed record AppSettings
{
    public LayoutSettings Layout { get; init; } = new();

    public LookSettings Look { get; init; } = new();

    /// <summary>Brings every value into its valid range (hand-edited or outdated files may contain anything).</summary>
    public AppSettings Normalize() => this with
    {
        Layout = (Layout ?? new LayoutSettings()).Normalize(),
        Look = (Look ?? new LookSettings()).Normalize(),
    };
}

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

    public LayoutSettings Normalize() => this with
    {
        LeftOffset = SettingsMath.ClampOrDefault(LeftOffset, MinLeftOffset, MaxLeftOffset, 8),
        MaxWidgetWidth = SettingsMath.ClampOrDefault(MaxWidgetWidth, MinWidgetWidth, MaxWidgetWidthLimit, 240),
        CoverSize = SettingsMath.ClampOrDefault(CoverSize, MinCoverSize, MaxCoverSize, 32),
    };
}

public enum ThemePreference
{
    System,
    Light,
    Dark,
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

    public CornerStyle FlyoutCorners { get; init; } = CornerStyle.Round;

    public bool UseAcrylic { get; init; } = true;

    public LookSettings Normalize() => this with
    {
        Theme = Enum.IsDefined(Theme) ? Theme : ThemePreference.System,
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? DefaultFontFamily : FontFamily.Trim(),
        TitleFontSize = SettingsMath.ClampOrDefault(TitleFontSize, MinFontSize, MaxFontSize, 12),
        ArtistFontSize = SettingsMath.ClampOrDefault(ArtistFontSize, MinFontSize, MaxFontSize, 11),
        TextColor = IsHexColor(TextColor) ? TextColor!.ToUpperInvariant() : null,
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
