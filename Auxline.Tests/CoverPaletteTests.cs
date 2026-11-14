using System.Windows.Media;
using Auxline.Controls;

namespace Auxline.Tests;

public sealed class CoverPaletteTests
{
    private static byte[] Pixels(params (Color Color, int Count)[] runs) =>
        runs.SelectMany(run => Enumerable.Repeat(new[] { run.Color.B, run.Color.G, run.Color.R, (byte)255 }, run.Count))
            .SelectMany(pixel => pixel)
            .ToArray();

    [Fact]
    public void SingleColour_IsDominant()
    {
        Assert.Equal(Color.FromRgb(196, 106, 46), CoverPalette.DominantColor(Pixels((Color.FromRgb(196, 106, 46), 10))));
    }

    [Fact]
    public void ColourfulPartWins_OverLargeDarkArea()
    {
        // 90 % near-black, 10 % red logo: the result is reddish, not dark grey.
        var dominant = CoverPalette.DominantColor(Pixels((Color.FromRgb(10, 10, 10), 90), (Color.FromRgb(220, 30, 30), 10)))!.Value;

        var hsv = HsvColor.FromRgb(dominant.R, dominant.G, dominant.B);
        Assert.InRange(hsv.Hue, 0, 10);
        Assert.True(hsv.Saturation > 0.5, $"saturation {hsv.Saturation}");
    }

    [Fact]
    public void AllGrey_StaysGrey()
    {
        Assert.Equal(Color.FromRgb(128, 128, 128), CoverPalette.DominantColor(Pixels((Color.FromRgb(128, 128, 128), 5))));
    }

    [Fact]
    public void NoPixels_IsNull()
    {
        Assert.Null(CoverPalette.DominantColor([]));
    }

    [Theory]
    [InlineData(196, 106, 46, false)] // warm orange cover, dark theme
    [InlineData(196, 106, 46, true)]
    [InlineData(20, 60, 200, false)] // deep blue
    [InlineData(20, 60, 200, true)]
    [InlineData(240, 230, 60, false)] // bright yellow
    [InlineData(240, 230, 60, true)]
    [InlineData(128, 128, 128, false)] // grey
    [InlineData(128, 128, 128, true)]
    public void ForTheme_AccentStaysReadableOnTint(byte red, byte green, byte blue, bool isLight)
    {
        var (tint, accent) = CoverPalette.ForTheme(Color.FromRgb(red, green, blue), isLight);

        // The accent fills the play button and the progress bar on the tinted background: at least 3:1 (WCAG UI contrast).
        Assert.True(ContrastRatio(tint, accent) >= 3, $"contrast {ContrastRatio(tint, accent):0.00}");
    }

    [Fact]
    public void ForTheme_KeepsTheCoversHue()
    {
        var (tint, accent) = CoverPalette.ForTheme(Color.FromRgb(20, 60, 200), isLight: false);

        Assert.InRange(HsvColor.FromRgb(tint.R, tint.G, tint.B).Hue, 220, 235);
        Assert.InRange(HsvColor.FromRgb(accent.R, accent.G, accent.B).Hue, 220, 235);
    }

    private static double ContrastRatio(Color first, Color second)
    {
        var (lighter, darker) = (Luminance(first), Luminance(second)) is var (a, b) && a > b ? (a, b) : (b, a);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
