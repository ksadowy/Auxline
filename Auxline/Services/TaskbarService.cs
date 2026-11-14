using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using Microsoft.Win32;
using static Auxline.Interop.NativeMethods;

namespace Auxline.Services;

/// <summary>
/// Primary taskbar: window handle, geometry and theme, plus a foreground-window hook.
/// Create on the UI thread: the WinEvent hook delivers callbacks through that thread's message loop.
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

    /// <returns>Primary taskbar window, or IntPtr.Zero when Explorer isn't running.</returns>
    public IntPtr FindTaskbarWindow() => FindWindow(TaskbarWindowClass, null);

    /// <summary>
    /// Buttons on the taskbar (Start, search, pinned/running apps, tray icons) via UI Automation.
    /// A cross-process query (~75 ms), so call it off the UI thread.
    /// </summary>
    /// <returns>Spans in physical pixels, or null when they can't be read (layout unknown).</returns>
    public IReadOnlyList<ButtonSpan>? GetButtonSpans()
    {
        var taskbarWindow = FindTaskbarWindow();
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

                return buttons.Cast<AutomationElement>()
                    .Where(button => !(bool)button.GetCachedPropertyValue(AutomationElement.IsOffscreenProperty))
                    .Select(button => (Rect)button.GetCachedPropertyValue(AutomationElement.BoundingRectangleProperty))
                    .Where(bounds => !bounds.IsEmpty && bounds.Width > 0)
                    .Select(bounds => new ButtonSpan(bounds.Left, bounds.Right))
                    .ToList();
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            // Explorer restarting or the taskbar redrawing mid-query.
            Trace.TraceWarning($"Reading taskbar buttons failed: {ex.Message}");
            return null;
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
