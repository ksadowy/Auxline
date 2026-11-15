using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Auxline.Controls;

/// <summary>Colours derived from album art, for the cover-tinted flyout style.</summary>
public static class CoverPalette
{
    /// <summary>Covers are scaled down to this many pixels per side first: enough for an average, cheap to scan.</summary>
    private const int SampleSize = 24;

    /// <summary>Lets an all-grey cover still produce a (grey) result instead of dividing by zero.</summary>
    private const double BaselineWeight = 0.02;

    /// <returns>Dominant colour of the image, or null when it can't be read.</returns>
    public static Color? DominantColor(BitmapSource image)
    {
        if (image.PixelWidth == 0 || image.PixelHeight == 0) return null;

        var scaled = new TransformedBitmap(image, new ScaleTransform(
            (double)SampleSize / image.PixelWidth, (double)SampleSize / image.PixelHeight));
        var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        return DominantColor(pixels);
    }

    /// <summary>
    /// Average of BGRA pixels, weighted by saturation × brightness: the colourful parts of a cover decide the colour,
    /// so a red logo on a mostly black cover still gives red, not dark grey.
    /// </summary>
    /// <returns>Null for no pixels.</returns>
    public static Color? DominantColor(byte[] bgraPixels)
    {
        double red = 0, green = 0, blue = 0, totalWeight = 0;
        for (var i = 0; i + 3 < bgraPixels.Length; i += 4)
        {
            byte b = bgraPixels[i], g = bgraPixels[i + 1], r = bgraPixels[i + 2];
            var hsv = HsvColor.FromRgb(r, g, b);
            var weight = hsv.Saturation * hsv.Value + BaselineWeight;
            red += r * weight;
            green += g * weight;
            blue += b * weight;
            totalWeight += weight;
        }

        if (totalWeight == 0) return null;
        return Color.FromRgb(ToByte(red / totalWeight), ToByte(green / totalWeight), ToByte(blue / totalWeight));
    }

    /// <summary>
    /// A background tint and an accent in the cover's hue. Dark theme: deep tint, bright accent; light theme: pale
    /// tint, deep accent. Saturation is kept in a range that stays readable, whatever the cover.
    /// </summary>
    public static (Color Tint, Color Accent) ForTheme(Color dominant, bool isLight)
    {
        var hsv = HsvColor.FromRgb(dominant.R, dominant.G, dominant.B);
        var hue = hsv.Hue;
        var saturation = hsv.Saturation;

        var tint = isLight
            ? new HsvColor(hue, Math.Min(saturation, 0.6) * 0.3, 0.96)
            : new HsvColor(hue, Math.Min(saturation, 0.7) * 0.85, 0.22);
        var accent = isLight
            ? new HsvColor(hue, Math.Clamp(saturation, 0.5, 0.85), 0.5)
            : new HsvColor(hue, Math.Clamp(saturation, 0.35, 0.65), 0.95);
        return (ToColor(tint), ToColor(accent));
    }

    private static Color ToColor(HsvColor hsv)
    {
        var (red, green, blue) = hsv.ToRgb();
        return Color.FromRgb(red, green, blue);
    }

    private static byte ToByte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 255));
}
