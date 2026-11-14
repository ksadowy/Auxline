using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Auxline.Controls;

/// <summary>
/// Turns an image into an opacity mask of its brightness, so a solid colour drawn through it shows the image in
/// that one colour (Terminal theme: the cover in phosphor green).
/// </summary>
public sealed class LuminanceMaskConverter : IValueConverter
{
    /// <summary>Even black parts stay faintly visible, like an unlit phosphor.</summary>
    private const int MinimumAlpha = 40;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BitmapSource image ? CreateMask(image) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static BitmapSource CreateMask(BitmapSource image)
    {
        var gray = new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0);
        var (width, height) = (gray.PixelWidth, gray.PixelHeight);
        var luminance = new byte[width * height];
        gray.CopyPixels(luminance, width, 0);

        // Black pixels whose alpha is the brightness (Bgra32: alpha is the 4th byte).
        var pixels = new byte[luminance.Length * 4];
        for (var i = 0; i < luminance.Length; i++)
        {
            pixels[i * 4 + 3] = (byte)(MinimumAlpha + luminance[i] * (255 - MinimumAlpha) / 255);
        }

        var mask = BitmapSource.Create(width, height, gray.DpiX, gray.DpiY, PixelFormats.Bgra32, null, pixels, width * 4);
        mask.Freeze();
        return mask;
    }
}
