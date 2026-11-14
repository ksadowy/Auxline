using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Auxline.Interop;

/// <summary>
/// Dark/light for native Win32 menus (the tray menu), which otherwise always draw light.
/// Uses undocumented uxtheme exports, addressed by ordinal (Windows 10 1903+; widely used by other apps).
/// Only affects Win32 menus: WPF windows draw themselves.
/// </summary>
internal static class NativeMenuTheme
{
    private enum PreferredAppMode
    {
        ForceDark = 2,
        ForceLight = 3,
    }

    /// <summary>Never throws: if Windows lacks these exports, menus simply stay light.</summary>
    public static void Apply(bool isDark)
    {
        try
        {
            SetPreferredAppMode(isDark ? PreferredAppMode.ForceDark : PreferredAppMode.ForceLight);
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            Trace.TraceWarning($"Dark menus unavailable on this Windows version: {ex.Message}");
        }
    }

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(PreferredAppMode mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();
}
