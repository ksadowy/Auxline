using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Win32;
using static Auxline.Interop.NativeMethods;

namespace Auxline.Services;

/// <summary>Screen edge the taskbar is docked to; values match the Win32 ABE_* constants.</summary>
public enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

/// <param name="Bounds">Docked position in physical pixels (also while an auto-hidden taskbar is slid away).</param>
public readonly record struct TaskbarPlacement(Int32Rect Bounds, TaskbarEdge Edge)
{
    /// <summary>Left/right taskbars (only via third-party tools on Windows 11): icons run top to bottom.</summary>
    public bool IsVertical => Edge is TaskbarEdge.Left or TaskbarEdge.Right;
}

/// <param name="Window">The taskbar window (primary: Shell_TrayWnd, others: Shell_SecondaryTrayWnd).</param>
/// <param name="MonitorDevice">Stable-ish monitor ID, e.g. "\\.\DISPLAY1"; what the settings store.</param>
/// <param name="WorkArea">The monitor's area without taskbars, in physical pixels.</param>
public sealed record TaskbarInfo(IntPtr Window, string MonitorDevice, bool IsPrimary, TaskbarPlacement Placement, Int32Rect MonitorBounds, Int32Rect WorkArea);

/// <summary>
/// The taskbars (one per monitor with "show taskbar on all displays"): windows, geometry and theme, plus a
/// foreground-window hook. Create on the UI thread: the WinEvent hook delivers callbacks through that thread's loop.
/// </summary>
public sealed class TaskbarService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // Kept in a field so the GC doesn't collect the delegate while native code still calls it.
    private readonly WinEventProc _foregroundCallback;
    private readonly IntPtr _foregroundHook;
    private bool _wasLightTheme;

    /// <summary>Raised whenever any window becomes the foreground window (including the taskbar itself).</summary>
    public event EventHandler? ForegroundChanged;

    /// <summary>
    /// Raised when Windows switches between light and dark (Settings → Personalization → Colors).
    /// Raised on a background thread: handlers must marshal to the UI thread.
    /// </summary>
    public event EventHandler? ThemeChanged;

    public TaskbarService()
    {
        _foregroundCallback = (_, _, _, _, _, _, _) => ForegroundChanged?.Invoke(this, EventArgs.Empty);
        _foregroundHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _foregroundCallback, 0, 0, WINEVENT_OUTOFCONTEXT);

        if (_foregroundHook == IntPtr.Zero)
        {
            Trace.TraceWarning("Foreground hook failed; the taskbar may occasionally cover the widget.");
        }

        _wasLightTheme = UsesLightTheme();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>All taskbars, primary first. Empty when Explorer isn't running.</summary>
    public IReadOnlyList<TaskbarInfo> GetTaskbars()
    {
        var windows = new List<IntPtr>();
        var primary = FindWindow(TaskbarWindowClass, null);
        if (primary != IntPtr.Zero) windows.Add(primary);

        var secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, SecondaryTaskbarWindowClass, null)) != IntPtr.Zero)
        {
            windows.Add(secondary);
        }

        return windows.Select(Describe).OfType<TaskbarInfo>().ToList();
    }

    /// <summary>
    /// The taskbar on the given monitor, or the primary one when <paramref name="monitorDevice"/> is null or that
    /// monitor (or its taskbar) is gone, e.g. unplugged, or "show taskbar on all displays" turned off.
    /// </summary>
    public TaskbarInfo? FindTaskbar(string? monitorDevice)
    {
        var taskbars = GetTaskbars();
        return taskbars.FirstOrDefault(t => monitorDevice is not null && t.MonitorDevice == monitorDevice)
            ?? taskbars.FirstOrDefault(t => t.IsPrimary)
            ?? taskbars.FirstOrDefault();
    }

    /// <summary>
    /// Buttons on a taskbar (Start, search, pinned/running apps, tray icons) via UI Automation.
    /// A cross-process query (~75 ms), so call it off the UI thread.
    /// </summary>
    /// <returns>
    /// Bounds in physical pixels, or null when they can't be read (layout unknown). An empty result also counts as
    /// unknown: a real taskbar always has at least Start and the clock, but UI Automation reports no buttons at all
    /// while the taskbar's context menu is open (for as long as it stays open) and for ~1 s while it re-lays out
    /// after being moved or realigned.
    /// </returns>
    public IReadOnlyList<Rect>? GetButtonBounds(IntPtr taskbarWindow)
    {
        if (taskbarWindow == IntPtr.Zero) return null;

        try
        {
            // Fetch rectangles and visibility in the same round trip instead of one call per button.
            var cache = new CacheRequest();
            cache.Add(AutomationElement.BoundingRectangleProperty);
            cache.Add(AutomationElement.IsOffscreenProperty);
            using (cache.Activate())
            {
                var buttons = AutomationElement.FromHandle(taskbarWindow).FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

                var bounds = buttons.Cast<AutomationElement>()
                    .Where(button => !(bool)button.GetCachedPropertyValue(AutomationElement.IsOffscreenProperty))
                    .Select(button => (Rect)button.GetCachedPropertyValue(AutomationElement.BoundingRectangleProperty))
                    .Where(rect => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
                    .ToList();
                return bounds.Count > 0 ? bounds : null;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            // Explorer restarting or the taskbar redrawing mid-query.
            Trace.TraceWarning($"Reading taskbar buttons failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The docked position is derived from the window and its monitor (<see cref="TaskbarGeometry.Dock"/>): the app bar
    /// query only knows the primary taskbar, and this also stays correct while an auto-hidden taskbar is slid away.
    /// </summary>
    private static TaskbarInfo? Describe(IntPtr window)
    {
        var monitor = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetWindowRect(window, out var windowRect) || !GetMonitorInfo(monitor, ref info)) return null;

        var monitorBounds = ToInt32Rect(info.rcMonitor);
        return new TaskbarInfo(
            window,
            info.szDevice,
            IsPrimary: (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
            TaskbarGeometry.Dock(ToInt32Rect(windowRect), monitorBounds),
            monitorBounds,
            ToInt32Rect(info.rcWork));
    }

    private static Int32Rect ToInt32Rect(RECT rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    /// <summary>The taskbar follows the system theme (not the app theme).</summary>
    public bool UsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }

    public void Dispose()
    {
        // SystemEvents is static: without unsubscribing, it would keep this instance alive.
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
    }

    /// <summary>Fires for many unrelated preferences, so only a real light/dark switch is passed on.</summary>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        var isLight = UsesLightTheme();
        if (isLight == _wasLightTheme) return;
        _wasLightTheme = isLight;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }
}
