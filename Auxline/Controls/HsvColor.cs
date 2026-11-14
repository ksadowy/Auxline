namespace Auxline.Controls;

/// <summary>
/// Hue (0–360°), saturation and value (0–1). The model behind the colour wheel:
/// angle = hue, distance from the centre = saturation, brightness slider = value.
/// </summary>
public readonly record struct HsvColor(double Hue, double Saturation, double Value)
{
    public (byte Red, byte Green, byte Blue) ToRgb()
    {
        var hue = ((Hue % 360) + 360) % 360;
        var chroma = Value * Saturation;
        var secondary = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var lightness = Value - chroma;

        var (red, green, blue) = (int)(hue / 60) switch
        {
            0 => (chroma, secondary, 0.0),
            1 => (secondary, chroma, 0.0),
            2 => (0.0, chroma, secondary),
            3 => (0.0, secondary, chroma),
            4 => (secondary, 0.0, chroma),
            _ => (chroma, 0.0, secondary),
        };
        return (ToByte(red + lightness), ToByte(green + lightness), ToByte(blue + lightness));
    }

    public static HsvColor FromRgb(byte red, byte green, byte blue)
    {
        double r = red / 255.0, g = green / 255.0, b = blue / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var delta = max - Math.Min(r, Math.Min(g, b));

        var hue = delta == 0 ? 0
            : max == r ? 60 * (((g - b) / delta % 6 + 6) % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        var saturation = max == 0 ? 0 : delta / max;
        return new HsvColor(hue, saturation, max);
    }

    private static byte ToByte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);
}
