namespace Auxline.Models;

/// <summary>User settings, persisted as JSON. Immutable: changes produce a new instance via <c>with</c>.</summary>
public sealed record AppSettings
{
    public LayoutSettings Layout { get; init; } = new();

    /// <summary>Brings every value into its valid range (hand-edited or outdated files may contain anything).</summary>
    public AppSettings Normalize() => this with { Layout = (Layout ?? new LayoutSettings()).Normalize() };
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
        LeftOffset = ClampOrDefault(LeftOffset, MinLeftOffset, MaxLeftOffset, 8),
        MaxWidgetWidth = ClampOrDefault(MaxWidgetWidth, MinWidgetWidth, MaxWidgetWidthLimit, 240),
        CoverSize = ClampOrDefault(CoverSize, MinCoverSize, MaxCoverSize, 32),
    };

    /// <summary>NaN/Infinity can't be clamped meaningfully, so they fall back to the default.</summary>
    private static double ClampOrDefault(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
