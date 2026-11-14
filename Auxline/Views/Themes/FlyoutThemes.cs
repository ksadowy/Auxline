using System.Windows;
using System.Windows.Media;
using Auxline.Models;
using Auxline.Views.FlyoutStyles;

namespace Auxline.Views.Themes;

/// <summary>What a theme decides about the flyout window itself, beyond its XAML resources.</summary>
/// <param name="Font">Null = the font chosen in the settings.</param>
/// <param name="Corners">Null = the corner style chosen in the settings.</param>
/// <param name="IsLight">
/// Null = follows the light/dark setting, and the cover-tinted style takes its colours from the album art. Otherwise
/// the theme has its own fixed palette, including the cover-tinted style's colours.
/// </param>
/// <param name="UsesSystemBackdrop">False: the theme paints its own background, so acrylic is off.</param>
internal sealed record FlyoutThemeTraits(FontFamily? Font, CornerStyle? Corners, bool? IsLight, bool UsesSystemBackdrop);

/// <summary>The flyout themes: a resource dictionary (colours, templates, icons) plus <see cref="FlyoutThemeTraits"/>.</summary>
internal static class FlyoutThemes
{
    private static readonly FontFamily PixelFont = new(new Uri("pack://application:,,,/Auxline;component/"), "./Fonts/#Silkscreen");

    public static FlyoutThemeTraits TraitsOf(FlyoutTheme theme) => theme switch
    {
        FlyoutTheme.GameBoy => new(PixelFont, CornerStyle.Square, IsLight: true, UsesSystemBackdrop: false),
        _ => new(Font: null, Corners: null, IsLight: null, UsesSystemBackdrop: true),
    };

    /// <summary>The theme's resources, to merge into the flyout window's.</summary>
    /// <param name="isLight">The light/dark setting, for themes that follow it.</param>
    public static ResourceDictionary CreateResources(FlyoutTheme theme, bool isLight)
    {
        if (theme == FlyoutTheme.GameBoy) return Load("GameBoyTheme.xaml");

        var resources = Load("DefaultTheme.xaml");
        FlyoutPalette.Apply(resources, isLight);
        return resources;
    }

    private static ResourceDictionary Load(string fileName) =>
        new() { Source = new Uri($"pack://application:,,,/Auxline;component/Views/Themes/{fileName}") };
}
