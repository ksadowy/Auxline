using Auxline.Models;
using static Auxline.Interop.NativeMethods;
using Color = System.Windows.Media.Color;

namespace Auxline.Interop;

/// <summary>Native Windows 11 window frame: acrylic backdrop, corner style, dark/light frame.</summary>
internal static class WindowBackdrop
{
    /// <returns>
    /// True when the acrylic backdrop is active. False when it's turned off or unsupported
    /// (Windows 10, Windows 11 before 22H2); the caller then paints a solid background.
    /// </returns>
    public static bool Apply(IntPtr hwnd, bool isDark, CornerStyle corners, bool useAcrylic)
    {
        SetAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, isDark ? 1 : 0);
        SetAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ToCornerPreference(corners));
        var isBackdropSet = SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, useAcrylic ? DWMSBT_TRANSIENTWINDOW : DWMSBT_NONE);
        return useAcrylic && isBackdropSet;
    }

    /// <summary>The thin border Windows 11 draws around the window; null = the system's default colour.</summary>
    public static void SetBorderColor(IntPtr hwnd, Color? color)
    {
        // COLORREF: 0x00BBGGRR.
        var value = color is { } c ? c.R | (c.G << 8) | (c.B << 16) : DWMWA_COLOR_DEFAULT;
        SetAttribute(hwnd, DWMWA_BORDER_COLOR, value);
    }

    private static int ToCornerPreference(CornerStyle corners) => corners switch
    {
        CornerStyle.Small => DWMWCP_ROUNDSMALL,
        CornerStyle.Square => DWMWCP_DONOTROUND,
        _ => DWMWCP_ROUND,
    };

    private static bool SetAttribute(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) >= 0;
}
