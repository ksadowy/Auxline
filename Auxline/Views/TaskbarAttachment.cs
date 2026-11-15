using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Auxline.Services;
using static Auxline.Interop.NativeMethods;

namespace Auxline.Views;

/// <summary>
/// Keeps a window glued to the taskbar: owned by it (so it's raised together with it), re-raised when the shell
/// raises the taskbar through other paths, and informed about fullscreen apps and taskbar/layout changes.
/// Placement itself stays with the window; this class only tells it when to re-place.
/// </summary>
internal sealed class TaskbarAttachment : IDisposable
{
    /// <summary>
    /// After the foreground changes, the shell sometimes raises the taskbar ~60-130 ms later through a path
    /// that ignores window ownership (seen after the snipping overlay closes). Raise the window after that.
    /// </summary>
    private static readonly TimeSpan RaiseDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Apps opening/closing change the foreground and shift centred taskbar icons, which slide into place;
    /// report a layout change once they've settled.
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(800);

    /// <summary>Broadcast by Explorer when it (re)creates the taskbar, e.g. after an Explorer restart.</summary>
    private static readonly int TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

    /// <summary>
    /// Sent by the shell to registered app bars; ABN_FULLSCREENAPP says a fullscreen app (game, video) opened or
    /// closed. The window is "always on top", so without this it would stay visible over fullscreen apps.
    /// </summary>
    private static readonly int AppBarNotifyMessage = RegisterWindowMessage("Auxline.AppBarNotify");

    private readonly Window _window;
    private readonly TaskbarService _taskbar;
    private readonly DispatcherTimer _raiseTimer;
    private readonly DispatcherTimer _settleTimer;

    // Kept in a field so the GC doesn't collect the delegate while native code still calls it.
    private readonly WinEventProc _taskbarMovedCallback;
    private IntPtr _taskbarMovedHook;

    private IntPtr _handle;
    private IntPtr _taskbarWindow;
    private string? _monitorDevice;
    private bool _isFullscreenAppOpen;
    private bool _isTaskbarSlidAway;

    /// <summary>Display/DPI change, Explorer restart, or apps opening/closing: taskbar icons may have moved.</summary>
    public event EventHandler? LayoutMayHaveChanged;

    /// <summary>Raised when <see cref="ShouldHide"/> changes.</summary>
    public event EventHandler<bool>? ShouldHideChanged;

    /// <summary>
    /// A fullscreen app covers the taskbar, or auto-hide slid the taskbar away. In the latter case staying visible
    /// would also block the mouse from touching the screen edge that brings the taskbar back.
    /// </summary>
    public bool ShouldHide => _isFullscreenAppOpen || _isTaskbarSlidAway;

    /// <summary>The taskbar this window sits on (falls back to the primary one if the chosen monitor is gone).</summary>
    public TaskbarInfo? CurrentTaskbar => _taskbar.FindTaskbar(_monitorDevice);

    /// <summary>Attach before the window is shown; the work starts once its handle exists.</summary>
    public TaskbarAttachment(Window window, TaskbarService taskbar)
    {
        _window = window;
        _taskbar = taskbar;

        _raiseTimer = new DispatcherTimer { Interval = RaiseDelay };
        _raiseTimer.Tick += (_, _) =>
        {
            _raiseTimer.Stop();
            RaiseAboveTaskbar();
            // Safety net in case a fullscreen open/close notification was missed.
            SetFullscreenAppOpen(IsFullscreenAppRunning() && IsForegroundFullscreenOnMyMonitor());
        };
        _settleTimer = new DispatcherTimer { Interval = SettleDelay };
        _settleTimer.Tick += (_, _) =>
        {
            _settleTimer.Stop();
            LayoutMayHaveChanged?.Invoke(this, EventArgs.Empty);
        };

        _taskbarMovedCallback = (_, _, hwnd, idObject, idChild, _, _) =>
        {
            if (hwnd == _taskbarWindow && idObject == OBJID_WINDOW && idChild == 0) UpdateTaskbarSlidAway();
        };

        _window.SourceInitialized += OnSourceInitialized;
        _taskbar.ForegroundChanged += OnForegroundChanged;
    }

    public void RaiseAboveTaskbar() =>
        SetWindowPos(_handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>Moves to another monitor's taskbar (null = primary); applies live.</summary>
    public void SelectMonitor(string? monitorDevice)
    {
        if (monitorDevice == _monitorDevice) return;
        _monitorDevice = monitorDevice;
        if (_handle == IntPtr.Zero) return; // attached once the window's handle exists

        AttachToTaskbar();
        UpdateTaskbarSlidAway();
        LayoutMayHaveChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _taskbar.ForegroundChanged -= OnForegroundChanged;
        _raiseTimer.Stop();
        _settleTimer.Stop();
        StopWatchingTaskbarMoves();
        if (_handle != IntPtr.Zero) UnregisterAppBar();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(_window).Handle;

        // Hide from Alt+Tab and never steal focus from the app the user is working in.
        var exStyle = GetWindowLongPtr(_handle, GWL_EXSTYLE);
        SetWindowLongPtr(_handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        HwndSource.FromHwnd(_handle).AddHook(WndProc);
        AttachToTaskbar();
        RegisterAppBar();
        // Started while a game/video is fullscreen or the taskbar is auto-hidden: no change event will come. Deferred
        // because this runs inside Show(), which would undo a Hide() made by a handler right now.
        _window.Dispatcher.BeginInvoke(() =>
        {
            SetFullscreenAppOpen(IsFullscreenAppRunning() && IsForegroundFullscreenOnMyMonitor());
            UpdateTaskbarSlidAway();
        });
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            LayoutMayHaveChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (msg == WM_SETTINGCHANGE)
        {
            // Moving the taskbar or changing its alignment doesn't change the foreground window, but does broadcast
            // this (work area, tray settings). Re-place now, and once more after the icons have slid into place.
            LayoutMayHaveChanged?.Invoke(this, EventArgs.Empty);
            _settleTimer.Stop();
            _settleTimer.Start();
        }
        else if (msg == TaskbarCreatedMessage)
        {
            // A restarted Explorer forgets the ownership and the app bar registration, and runs in a new process.
            AttachToTaskbar();
            RegisterAppBar();
            UpdateTaskbarSlidAway();
            LayoutMayHaveChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (msg == AppBarNotifyMessage && wParam == ABN_FULLSCREENAPP)
        {
            // The notification doesn't say which monitor; a fullscreen video on the other display shouldn't hide us.
            SetFullscreenAppOpen(lParam != IntPtr.Zero && IsForegroundFullscreenOnMyMonitor());
        }
        return IntPtr.Zero;
    }

    /// <summary>Restart the one-shot timers, so a burst of foreground changes causes a single raise and report.</summary>
    private void OnForegroundChanged(object? sender, EventArgs e)
    {
        _raiseTimer.Stop();
        _raiseTimer.Start();
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    /// <summary>
    /// Windows keeps owned windows above their owner, so whenever the taskbar raises itself
    /// (clicks, Start, snipping overlay) this window is raised with it. The delayed raise covers the rest.
    /// </summary>
    private void AttachToTaskbar()
    {
        _taskbarWindow = CurrentTaskbar?.Window ?? IntPtr.Zero;
        if (_taskbarWindow == IntPtr.Zero)
        {
            Trace.TraceWarning("Taskbar window not found; widget may end up behind the taskbar.");
            return;
        }
        SetWindowLongPtr(_handle, GWLP_HWNDPARENT, _taskbarWindow);
        WatchTaskbarMoves();
    }

    /// <summary>
    /// With auto-hide, Windows 11 moves the taskbar window itself: it jumps back to its docked position when shown
    /// and slides down in steps (~280 ms) when hidden, leaving 2 px on screen. Location events are limited to
    /// Explorer's process, so mouse movement and other apps don't wake us up.
    /// </summary>
    private void WatchTaskbarMoves()
    {
        StopWatchingTaskbarMoves();
        GetWindowThreadProcessId(_taskbarWindow, out var explorerProcessId);
        _taskbarMovedHook = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _taskbarMovedCallback,
            explorerProcessId, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

        if (_taskbarMovedHook == IntPtr.Zero)
        {
            Trace.TraceWarning("Taskbar move hook failed; the widget won't follow an auto-hiding taskbar.");
        }
    }

    private void StopWatchingTaskbarMoves()
    {
        if (_taskbarMovedHook == IntPtr.Zero) return;
        UnhookWinEvent(_taskbarMovedHook);
        _taskbarMovedHook = IntPtr.Zero;
    }

    /// <summary>Anywhere but its docked position counts as slid away or sliding.</summary>
    private void UpdateTaskbarSlidAway()
    {
        if (!GetWindowRect(_taskbarWindow, out var actual) || CurrentTaskbar is not { } taskbar) return;

        var docked = taskbar.Placement.Bounds;
        var isSlidAway = actual.Left != docked.X || actual.Top != docked.Y;
        if (isSlidAway == _isTaskbarSlidAway) return;
        UpdateShouldHide(() => _isTaskbarSlidAway = isSlidAway);
    }

    /// <summary>
    /// Registers only to receive shell notifications (fullscreen open/close). No ABM_SETPOS follows, so no screen
    /// space is reserved and the work area is untouched.
    /// </summary>
    private void RegisterAppBar()
    {
        var data = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd = _handle,
            uCallbackMessage = (uint)AppBarNotifyMessage,
        };
        SHAppBarMessage(ABM_NEW, ref data);
    }

    private void UnregisterAppBar()
    {
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = _handle };
        SHAppBarMessage(ABM_REMOVE, ref data);
    }

    private static bool IsFullscreenAppRunning() =>
        SHQueryUserNotificationState(out var state) >= 0
        && state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;

    /// <summary>The active window covers this taskbar's whole monitor (only meaningful while a fullscreen app runs).</summary>
    private bool IsForegroundFullscreenOnMyMonitor()
    {
        if (CurrentTaskbar is not { } taskbar || !GetWindowRect(GetForegroundWindow(), out var foreground)) return false;

        var monitor = taskbar.MonitorBounds;
        return foreground.Left <= monitor.X && foreground.Top <= monitor.Y
            && foreground.Right >= monitor.X + monitor.Width && foreground.Bottom >= monitor.Y + monitor.Height;
    }

    private void SetFullscreenAppOpen(bool isOpen)
    {
        if (isOpen == _isFullscreenAppOpen) return;
        UpdateShouldHide(() => _isFullscreenAppOpen = isOpen);
    }

    /// <summary>Applies a state change and reports it only if the combined <see cref="ShouldHide"/> flipped.</summary>
    private void UpdateShouldHide(Action applyChange)
    {
        var wasHidden = ShouldHide;
        applyChange();
        if (ShouldHide != wasHidden) ShouldHideChanged?.Invoke(this, ShouldHide);
    }
}
