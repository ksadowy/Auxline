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
/// Not embedded in Explorer: a topmost tool window that never takes focus.
/// </summary>
public partial class WidgetWindow : Window
{
    /// <summary>Gap from the taskbar's left edge. Becomes a user setting in Phase 5.</summary>
    private const double LeftOffsetDip = 8;

    private readonly TaskbarService _taskbar;
    private IntPtr _handle;

    public WidgetWindow(TaskbarService taskbar)
    {
        InitializeComponent();
        _taskbar = taskbar;
        Foreground = taskbar.UsesLightTheme() ? Brushes.Black : Brushes.White;

        SourceInitialized += OnSourceInitialized;
        DpiChanged += (_, _) => RepositionAfterLayout();
        _taskbar.ForegroundChanged += OnForegroundChanged;
        Closed += (_, _) => _taskbar.ForegroundChanged -= OnForegroundChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;

        // Hide from Alt+Tab and never steal focus from the app the user is working in.
        var exStyle = GetWindowLongPtr(_handle, GWL_EXSTYLE);
        SetWindowLongPtr(_handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        HwndSource.FromHwnd(_handle).AddHook(WndProc);
        Reposition();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE) RepositionAfterLayout();
        return IntPtr.Zero;
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

    /// <summary>Clicking the taskbar raises it above other topmost windows; put the widget back on top.</summary>
    private void OnForegroundChanged(object? sender, EventArgs e) =>
        SetWindowPos(_handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
}
