using static Auxline.Interop.NativeMethods;

namespace Auxline.Interop;

/// <summary>Native Windows 11 flyout look: acrylic backdrop, rounded corners, dark/light frame.</summary>
internal static class WindowBackdrop
{
    /// <returns>False when the acrylic backdrop is unsupported (Windows 10, Windows 11 before 22H2).</returns>
    public static bool TryApplyAcrylic(IntPtr hwnd, bool isDark)
    {
        SetAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, isDark ? 1 : 0);
        SetAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
        return SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW);
    }

    private static bool SetAttribute(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) >= 0;
}
