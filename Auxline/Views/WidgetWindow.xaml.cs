using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Auxline.Models;
using Auxline.Services;
using static Auxline.Interop.NativeMethods;

namespace Auxline.Views;

/// <summary>
/// Compact now-playing widget drawn over the left side of the taskbar.
/// Not embedded in Explorer: a tool window owned by the taskbar that never takes focus.
/// </summary>
public partial class WidgetWindow : Window
{
    /// <summary>Minimum space above and below the cover, so it never touches the taskbar's edges.</summary>
    private const double CoverVerticalMarginDip = 4;

    /// <summary>
    /// After the foreground changes, the shell sometimes raises the taskbar ~60-130 ms later through a path
    /// that ignores window ownership (seen after the snipping overlay closes). Raise the widget after that.
    /// </summary>
    private static readonly TimeSpan RaiseDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Apps opening/closing change the foreground and shift centred taskbar icons, which slide into place;
    /// re-measure the buttons once they've settled.
    /// </summary>
    private static readonly TimeSpan MeasureDelay = TimeSpan.FromMilliseconds(800);

    /// <summary>Space kept between the widget and taskbar buttons.</summary>
    private const double ButtonGapDip = 8;

    /// <summary>Below this the widget can't show a cover and readable text, so it hides instead.</summary>
    private const double MinUsefulWidthDip = 48;

    /// <summary>Broadcast by Explorer when it (re)creates the taskbar, e.g. after an Explorer restart.</summary>
    private static readonly int TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

    /// <summary>
    /// Sent by the shell to registered app bars; ABN_FULLSCREENAPP says a fullscreen app (game, video) opened or
    /// closed. The widget is "always on top", so without this it would stay visible over fullscreen apps.
    /// </summary>
    private static readonly int AppBarNotifyMessage = RegisterWindowMessage("Auxline.AppBarNotify");

    private readonly TaskbarService _taskbar;
    private readonly DispatcherTimer _raiseTimer;
    private readonly DispatcherTimer _measureTimer;
    private IntPtr _handle;
    private double _leftOffsetDip = new LayoutSettings().LeftOffset;
    private double _maxWidthSettingDip = new LayoutSettings().MaxWidgetWidth;

    /// <summary>
    /// Last measured taskbar buttons (null until the first measurement succeeds). Placement uses this synchronously,
    /// so moving the widget never waits for (or races) the slow UI Automation query.
    /// </summary>
    private IReadOnlyList<ButtonSpan>? _buttons;

    /// <summary>Only the newest measurement may apply; older ones finishing late are stale.</summary>
    private int _latestMeasureRequest;

    private bool _isHiddenForFullscreen;

    public event EventHandler? Clicked;

    /// <summary>Current bounds in DIPs.</summary>
    public Rect Bounds => new(Left, Top, ActualWidth, ActualHeight);

    public WidgetWindow(TaskbarService taskbar)
    {
        InitializeComponent();
        _taskbar = taskbar;

        SourceInitialized += OnSourceInitialized;
        MouseLeftButtonUp += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        DpiChanged += (_, _) => RepositionAfterLayout();

        _raiseTimer = new DispatcherTimer { Interval = RaiseDelay };
        _raiseTimer.Tick += (_, _) =>
        {
            _raiseTimer.Stop();
            RaiseAboveTaskbar();
            // Safety net in case a fullscreen open/close notification was missed.
            SetHiddenForFullscreen(IsFullscreenAppRunning());
        };
        _measureTimer = new DispatcherTimer { Interval = MeasureDelay };
        _measureTimer.Tick += (_, _) =>
        {
            _measureTimer.Stop();
            MeasureTaskbar();
        };
        _taskbar.ForegroundChanged += OnForegroundChanged;
        Closed += (_, _) =>
        {
            _taskbar.ForegroundChanged -= OnForegroundChanged;
            _raiseTimer.Stop();
            _measureTimer.Stop();
            UnregisterAppBar();
        };
    }

    /// <summary>Applies live; safe to call before the window is shown.</summary>
    public void ApplyLayout(LayoutSettings layout)
    {
        _leftOffsetDip = layout.LeftOffset;
        _maxWidthSettingDip = layout.MaxWidgetWidth;
        CoverImage.Width = layout.CoverSize;
        CoverImage.Height = layout.CoverSize;
        if (_handle != IntPtr.Zero) Reposition();
        else MaxWidth = _maxWidthSettingDip;
    }

    /// <summary>Applies live; safe to call before the window is shown.</summary>
    /// <param name="isTaskbarLight">The widget sits on the taskbar, so automatic text contrasts with it.</param>
    public void ApplyLook(LookSettings look, bool isTaskbarLight)
    {
        Foreground = look.TextColor is { } hex
            ? FrozenBrush((Color)ColorConverter.ConvertFromString(hex))
            : isTaskbarLight ? Brushes.Black : Brushes.White;
        FontFamily = new FontFamily(look.FontFamily);
        TitleText.FontSize = look.TitleFontSize;
        ArtistText.FontSize = look.ArtistFontSize;
    }

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;

        // Hide from Alt+Tab and never steal focus from the app the user is working in.
        var exStyle = GetWindowLongPtr(_handle, GWL_EXSTYLE);
        SetWindowLongPtr(_handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        HwndSource.FromHwnd(_handle).AddHook(WndProc);
        AttachToTaskbar();
        RegisterAppBar();
        Reposition();
        MeasureTaskbar();
        // Started while a game/video is already fullscreen: no "opened" notification will come. Deferred because
        // this runs inside Show(), which would otherwise immediately undo the Hide().
        Dispatcher.BeginInvoke(() => SetHiddenForFullscreen(IsFullscreenAppRunning()));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            RepositionAfterLayout();
            MeasureTaskbar();
        }
        else if (msg == TaskbarCreatedMessage)
        {
            // A restarted Explorer forgets both the ownership and the app bar registration.
            AttachToTaskbar();
            RegisterAppBar();
            RepositionAfterLayout();
            MeasureTaskbar();
        }
        else if (msg == AppBarNotifyMessage && wParam == ABN_FULLSCREENAPP)
        {
            SetHiddenForFullscreen(lParam != IntPtr.Zero);
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Windows keeps owned windows above their owner, so whenever the taskbar raises itself
    /// (clicks, Start, snipping overlay) the widget is raised with it. The delayed raise covers the rest.
    /// </summary>
    private void AttachToTaskbar()
    {
        var taskbarWindow = _taskbar.FindTaskbarWindow();
        if (taskbarWindow == IntPtr.Zero)
        {
            Trace.TraceWarning("Taskbar window not found; widget may end up behind the taskbar.");
            return;
        }
        SetWindowLongPtr(_handle, GWLP_HWNDPARENT, taskbarWindow);
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

    private void SetHiddenForFullscreen(bool isHidden)
    {
        if (isHidden == _isHiddenForFullscreen) return;
        _isHiddenForFullscreen = isHidden;

        if (isHidden)
        {
            Hide();
        }
        else
        {
            Show();
            RaiseAboveTaskbar();
        }
    }

    /// <summary>Defer until WPF has applied its own DPI/display handling, which would override our position.</summary>
    private void RepositionAfterLayout() => Dispatcher.BeginInvoke(Reposition, DispatcherPriority.Background);

    private void Reposition()
    {
        if (_taskbar.GetTaskbarBounds() is not { } taskbar)
        {
            Trace.TraceWarning("Taskbar not found; widget position unchanged.");
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Height = taskbar.Height / scale;
        // The cover-size setting is in DIPs, but the taskbar's DIP height shrinks with scaling (48 px at 125% ≈ 38 DIP).
        CoverImage.MaxHeight = CoverImage.MaxWidth = Math.Max(0, Height - CoverVerticalMarginDip * 2);

        // Never cover taskbar icons: stay in a free gap, stopping before the icons if the offset points into them.
        var requestedStart = taskbar.X + _leftOffsetDip * scale;
        var slot = _buttons is null
            ? new WidgetSlot(requestedStart, double.PositiveInfinity) // layout unknown: only the max-width setting applies
            : TaskbarFreeSpace.Fit(requestedStart, taskbar.X, taskbar.X + taskbar.Width, _buttons,
                ButtonGapDip * scale, MinUsefulWidthDip * scale);

        // Zero width hides the widget when no gap has room; it reappears once one does.
        var widthDip = Math.Min(_maxWidthSettingDip, slot.Width / scale);
        MaxWidth = slot.Width > 0 ? widthDip : 0;

        SetWindowPos(_handle, HWND_TOPMOST, (int)Math.Round(slot.Start), taskbar.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Re-reads taskbar button positions off the UI thread, then re-places the widget with them.</summary>
    private async void MeasureTaskbar()
    {
        var request = ++_latestMeasureRequest;
        var buttons = await Task.Run(_taskbar.GetButtonSpans);
        if (request != _latestMeasureRequest) return;

        // A failed read keeps the last known layout rather than dropping the protection.
        if (buttons is not null) _buttons = buttons;
        Reposition();
    }

    /// <summary>Restart the one-shot timers, so a burst of foreground changes causes a single raise and measurement.</summary>
    private void OnForegroundChanged(object? sender, EventArgs e)
    {
        _raiseTimer.Stop();
        _raiseTimer.Start();
        _measureTimer.Stop();
        _measureTimer.Start();
    }

    private void RaiseAboveTaskbar() =>
        SetWindowPos(_handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
}
