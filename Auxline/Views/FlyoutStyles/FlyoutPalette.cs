using System.Windows;
using System.Windows.Media;
using Auxline.Controls;

namespace Auxline.Views.FlyoutStyles;

/// <summary>
/// The colours flyout styles use (DynamicResource keys, see FlyoutResources.xaml) for light and dark.
/// This is where themes plug in later: a theme is another set of these values.
/// </summary>
internal static class FlyoutPalette
{
    public static void Apply(ResourceDictionary resources, bool isLight)
    {
        Set(resources, "FlyoutSecondaryTextBrush", isLight ? "#3A3A3A" : "#D0D0D0");
        Set(resources, "FlyoutTertiaryTextBrush", isLight ? "#5C5C5C" : "#A8A8A8");
        Set(resources, "FlyoutTrackBrush", isLight ? "#38000000" : "#4DFFFFFF");
        Set(resources, "FlyoutSliderFillBrush", isLight ? "#1F1F1F" : "#E6E6E6");
        Set(resources, "FlyoutHoverBrush", isLight ? "#14000000" : "#1FFFFFFF");
        Set(resources, "FlyoutPressedBrush", isLight ? "#24000000" : "#33FFFFFF");
        Set(resources, "FlyoutSeparatorBrush", isLight ? "#14000000" : "#14FFFFFF");
        Set(resources, "FlyoutPrimaryButtonBrush", isLight ? "#1A1A1A" : "#FFFFFF");
        Set(resources, "FlyoutPrimaryButtonForegroundBrush", isLight ? "#FFFFFF" : "#141414");

        // Windows' own accent colour, in the variant Windows uses for that theme (deep on light, bright on dark).
        resources["FlyoutAccentBrush"] = isLight ? SystemColors.AccentColorDark1Brush : SystemColors.AccentColorLight2Brush;
        Set(resources, "FlyoutAccentForegroundBrush", isLight ? "#FFFFFF" : "#000000");
    }

    /// <summary>Cover-tinted style: tint and accent from the album art (a neutral grey without one).</summary>
    public static void ApplyCover(ResourceDictionary resources, Color? coverColor, bool isLight)
    {
        var (tint, accent) = CoverPalette.ForTheme(coverColor ?? Color.FromRgb(128, 128, 128), isLight);
        resources["CoverTintBrush"] = Frozen(new SolidColorBrush(tint));
        resources["CoverAccentBrush"] = Frozen(new SolidColorBrush(accent));
        // The play button's icon sits on the accent: use the tint, which contrasts by construction.
        resources["CoverAccentForegroundBrush"] = resources["CoverTintBrush"];
        Set(resources, "CoverTextBrush", isLight ? "#1A1A1A" : "#FFFFFF");
        Set(resources, "CoverSecondaryTextBrush", isLight ? "#4A4A4A" : "#E2D6CE");
    }

    private static void Set(ResourceDictionary resources, string key, string color) =>
        resources[key] = Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)));

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
