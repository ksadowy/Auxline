using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using static Auxline.Interop.NativeMethods;

namespace Auxline.Services;

/// <summary>
/// Taskbar geometry and theme, plus a foreground-window hook used to keep the widget above the taskbar.
/// Create on the UI thread: the WinEvent hook delivers callbacks through that thread's message loop.
/// </summary>
public sealed class TaskbarService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // Kept in a field so the GC doesn't collect the delegate while native code still calls it.
    private readonly WinEventProc _foregroundCallback;
    private readonly IntPtr _foregroundHook;

    /// <summary>Raised whenever any window becomes the foreground window (including the taskbar itself).</summary>
    public event EventHandler? ForegroundChanged;

    public TaskbarService()
    {
        _foregroundCallback = (_, _, _, _, _, _, _) => ForegroundChanged?.Invoke(this, EventArgs.Empty);
        _foregroundHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _foregroundCallback, 0, 0, WINEVENT_OUTOFCONTEXT);

        if (_foregroundHook == IntPtr.Zero)
        {
            Trace.TraceWarning("Foreground hook failed; the taskbar may cover the widget after it is clicked.");
        }
    }

    /// <returns>Primary taskbar bounds in physical pixels, or null when Explorer reports no taskbar.</returns>
    public Int32Rect? GetTaskbarBounds()
    {
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref data) == UIntPtr.Zero) return null;

        var rect = data.rc;
        return new Int32Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>The taskbar follows the system theme (not the app theme).</summary>
    public bool UsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }

    public void Dispose()
    {
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
    }
}
