using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Auxline.Services;
using static Auxline.Interop.NativeMethods;

namespace Auxline.Views;

/// <summary>
/// Compact now-playing widget drawn over the left side of the taskbar.
/// Not embedded in Explorer: a tool window owned by the taskbar that never takes focus.
/// </summary>
public partial class WidgetWindow : Window
{
    /// <summary>Gap from the taskbar's left edge. Becomes a user setting in Phase 5.</summary>
    private const double LeftOffsetDip = 8;

    /// <summary>
    /// After the foreground changes, the shell sometimes raises the taskbar ~60-130 ms later through a path
    /// that ignores window ownership (seen after the snipping overlay closes). Raise the widget after that.
    /// </summary>
    private static readonly TimeSpan RaiseDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Broadcast by Explorer when it (re)creates the taskbar, e.g. after an Explorer restart.</summary>
    private static readonly int TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

    private readonly TaskbarService _taskbar;
    private readonly DispatcherTimer _raiseTimer;
    private IntPtr _handle;

    public event EventHandler? Clicked;

    /// <summary>Current bounds in DIPs.</summary>
    public Rect Bounds => new(Left, Top, ActualWidth, ActualHeight);

    public WidgetWindow(TaskbarService taskbar)
    {
        InitializeComponent();
        _taskbar = taskbar;
        Foreground = taskbar.UsesLightTheme() ? Brushes.Black : Brushes.White;

        SourceInitialized += OnSourceInitialized;
        MouseLeftButtonUp += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        DpiChanged += (_, _) => RepositionAfterLayout();

        _raiseTimer = new DispatcherTimer { Interval = RaiseDelay };
        _raiseTimer.Tick += (_, _) =>
        {
            _raiseTimer.Stop();
            RaiseAboveTaskbar();
        };
        _taskbar.ForegroundChanged += OnForegroundChanged;
        Closed += (_, _) =>
        {
            _taskbar.ForegroundChanged -= OnForegroundChanged;
            _raiseTimer.Stop();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;

        // Hide from Alt+Tab and never steal focus from the app the user is working in.
        var exStyle = GetWindowLongPtr(_handle, GWL_EXSTYLE);
        SetWindowLongPtr(_handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        HwndSource.FromHwnd(_handle).AddHook(WndProc);
        AttachToTaskbar();
        Reposition();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            RepositionAfterLayout();
        }
        else if (msg == TaskbarCreatedMessage)
        {
            AttachToTaskbar();
            RepositionAfterLayout();
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
        var x = taskbar.X + (int)Math.Round(LeftOffsetDip * scale);
        SetWindowPos(_handle, HWND_TOPMOST, x, taskbar.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Restart the one-shot timer, so a burst of foreground changes causes a single raise.</summary>
    private void OnForegroundChanged(object? sender, EventArgs e)
    {
        _raiseTimer.Stop();
        _raiseTimer.Start();
    }

    private void RaiseAboveTaskbar() =>
        SetWindowPos(_handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
}
