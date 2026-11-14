using System.Windows;
using System.Windows.Media;
using Auxline.Controls;
using Auxline.Models;
using Auxline.Views.FlyoutStyles;

namespace Auxline.Views.Themes;

/// <summary>
/// A theme with its own fixed look. It replaces the font, corner and light/dark settings, paints its own background
/// (acrylic off) and defines the cover-tinted style's colours itself instead of taking them from the album art.
/// </summary>
/// <param name="BorderColor">The thin window border Windows 11 draws.</param>
internal sealed record FixedLook(FontFamily Font, CornerStyle Corners, bool IsLight, Color BorderColor);

/// <summary>The flyout themes: a resource dictionary (colours, templates, icons), plus a <see cref="FixedLook"/> for all but Default.</summary>
internal static class FlyoutThemes
{
    private static readonly Uri FontBase = new("pack://application:,,,/Auxline;component/");

    /// <summary>Null for the default theme, which follows the settings.</summary>
    public static FixedLook? FixedLookOf(FlyoutTheme theme) => theme switch
    {
        FlyoutTheme.GameBoy => new(new FontFamily(FontBase, "./Fonts/#Silkscreen"), CornerStyle.Square, IsLight: true, Rgb(0x0F380F)),
        FlyoutTheme.Arcade => new(new FontFamily(FontBase, "./Fonts/#Press Start 2P"), CornerStyle.Square, IsLight: false, Rgb(0x4FF0FF)),
        FlyoutTheme.Vinyl => new(new FontFamily("Bahnschrift"), CornerStyle.Round, IsLight: false, Rgb(0x4A3221)),
        FlyoutTheme.Terminal => new(new FontFamily("Cascadia Mono, Consolas"), CornerStyle.Small, IsLight: false, Rgb(0x1F7A3A)),
        FlyoutTheme.Orbit => new(new FontFamily("Bahnschrift"), CornerStyle.Round, IsLight: false, Rgb(0x2C4366)),
        _ => null,
    };

    /// <summary>The widget's music meter in the theme's style (Default: the widget's text colour).</summary>
    public static MeterLook MeterOf(FlyoutTheme theme) => theme switch
    {
        FlyoutTheme.GameBoy => new(MeterShape.Bars, BarCount: 3, BarWidth: 4, Gap: 2, Steps: 4, IsRounded: false, Rgb(0x9BBC0F)),
        FlyoutTheme.Arcade => new(MeterShape.Bars, BarCount: 5, BarWidth: 3, Gap: 2, Steps: 0, IsRounded: false, Rgb(0x4FF0FF)),
        FlyoutTheme.Vinyl => new(MeterShape.Needle, BarCount: 0, BarWidth: 0, Gap: 0, Steps: 0, IsRounded: true, Rgb(0xE8A33D)),
        FlyoutTheme.Terminal => new(MeterShape.Bars, BarCount: 4, BarWidth: 4, Gap: 1, Steps: 4, IsRounded: false, Rgb(0x33FF66)),
        FlyoutTheme.Orbit => new(MeterShape.Bars, BarCount: 4, BarWidth: 1.5, Gap: 3, Steps: 0, IsRounded: true, Rgb(0x6FA8F5)),
        _ => LevelMeter.DefaultLook,
    };

    /// <summary>The theme's resources, to merge into the flyout window's.</summary>
    /// <param name="isLight">The light/dark setting (only the default theme follows it).</param>
    public static ResourceDictionary CreateResources(FlyoutTheme theme, bool isLight)
    {
        if (theme != FlyoutTheme.Default) return Load($"{theme}Theme.xaml");

        var resources = Load("DefaultTheme.xaml");
        FlyoutPalette.Apply(resources, isLight);
        return resources;
    }

    private static ResourceDictionary Load(string fileName) =>
        new() { Source = new Uri($"pack://application:,,,/Auxline;component/Views/Themes/{fileName}") };

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
