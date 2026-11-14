using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Auxline.Interop;
using Auxline.Models;
using Auxline.Services;
using Auxline.ViewModels;
using static Auxline.Interop.NativeMethods;

namespace Auxline.Views;

/// <summary>
/// Popup above the widget with cover art, progress, controls and volume. Reused: hidden, never closed.
/// Closes when it loses focus (click anywhere else) or on Esc.
/// </summary>
public partial class FlyoutWindow : Window
{
    /// <summary>Space between the flyout and the widget (taskbar).</summary>
    private const double GapToAnchorDip = 12;

    /// <summary>
    /// Clicking the widget while the flyout is open first deactivates (hides) the flyout on mouse down, then raises
    /// the widget's click on mouse up. A toggle this soon after a focus-loss hide is that same click: stay closed.
    /// </summary>
    private const long ReopenGuardMilliseconds = 300;

    /// <summary>Used when acrylic is off or unsupported; same tints as Windows 11 flyouts.</summary>
    private static readonly Brush LightSolidBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)));
    private static readonly Brush DarkSolidBackground = Freeze(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)));

    private LookSettings _look = new();
    private bool _isLight;
    private IntPtr _handle;
    private Rect _anchor;
    private TaskbarEdge _taskbarEdge = TaskbarEdge.Bottom;
    private Rect _workArea = SystemParameters.WorkArea;
    /// <summary>Tick count of the last focus-loss hide; null until it first happens.</summary>
    private long? _hiddenByFocusLossAt;

    private PlayerViewModel Player => (PlayerViewModel)DataContext;

    public FlyoutWindow()
    {
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        Deactivated += (_, _) =>
        {
            _hiddenByFocusLossAt = Environment.TickCount64;
            Hide();
        };
        KeyDown += OnKeyDown;
        IsVisibleChanged += (_, _) => Player.SetProgressShown(IsVisible);
        // Content can grow while open (e.g. the progress bar appears); keep the edge facing the widget in place.
        SizeChanged += (_, e) =>
        {
            if (IsVisible && e.HeightChanged) PlaceNextToAnchor(ActualHeight);
        };
    }

    /// <summary>Applies live; safe to call before the window is shown.</summary>
    /// <param name="isLight">Resolved theme (the "System" preference already replaced by the actual system theme).</param>
    public void ApplyLook(LookSettings look, bool isLight)
    {
        _look = look;
        _isLight = isLight;
        Foreground = isLight ? Brushes.Black : Brushes.White;
        FontFamily = new FontFamily(look.FontFamily);
        if (_handle != IntPtr.Zero) ApplyFrame();
    }

    /// <param name="anchor">Widget bounds in DIPs.</param>
    /// <param name="taskbarEdge">The flyout opens on the side facing away from it (above a bottom taskbar, etc.).</param>
    /// <param name="workArea">The widget's monitor (DIPs); the flyout is kept inside it.</param>
    public void Toggle(Rect anchor, TaskbarEdge taskbarEdge, Rect workArea)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }
        if (_hiddenByFocusLossAt is { } hiddenAt && Environment.TickCount64 - hiddenAt < ReopenGuardMilliseconds) return;

        // Measure before showing so the flyout appears at its final position, without a jump.
        _anchor = anchor;
        _taskbarEdge = taskbarEdge;
        _workArea = workArea;
        var content = (FrameworkElement)Content;
        content.Measure(new Size(Width, double.PositiveInfinity));
        PlaceNextToAnchor(content.DesiredSize.Height);

        Show();
        Activate();
    }

    private void PlaceNextToAnchor(double height)
    {
        (Left, Top) = _taskbarEdge switch
        {
            TaskbarEdge.Top => (_anchor.Left, _anchor.Bottom + GapToAnchorDip),
            TaskbarEdge.Left => (_anchor.Right + GapToAnchorDip, _anchor.Top),
            TaskbarEdge.Right => (_anchor.Left - Width - GapToAnchorDip, _anchor.Top),
            _ => (_anchor.Left, _anchor.Top - height - GapToAnchorDip),
        };

        // Keep it on the widget's monitor: next to a vertical taskbar it hangs down from the widget, and docked right
        // near the screen edge it would stick out sideways.
        Top = Math.Max(_workArea.Top, Math.Min(Top, _workArea.Bottom - height));
        Left = Math.Max(_workArea.Left, Math.Min(Left, _workArea.Right - Width));
    }

    private void OnProgressPressed(object sender, MouseButtonEventArgs e) => Player.IsSeeking = true;

    /// <summary>Seek once on release (click or end of drag), not on every value change while dragging.</summary>
    private void OnProgressReleased(object sender, MouseButtonEventArgs e)
    {
        Player.IsSeeking = false;
        Player.SeekCommand.Execute(null);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;

        var exStyle = GetWindowLongPtr(_handle, GWL_EXSTYLE);
        SetWindowLongPtr(_handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

        // Without a system menu DWM draws no caption buttons (the red X) on the extended frame.
        var style = GetWindowLongPtr(_handle, GWL_STYLE);
        SetWindowLongPtr(_handle, GWL_STYLE, style & ~(nint)WS_SYSMENU);

        // WPF paints nothing itself, so either the DWM backdrop or our solid Background shows.
        HwndSource.FromHwnd(_handle).CompositionTarget.BackgroundColor = Colors.Transparent;
        ApplyFrame();
    }

    private void ApplyFrame()
    {
        var hasAcrylic = WindowBackdrop.Apply(_handle, isDark: !_isLight, _look.FlyoutCorners, _look.UseAcrylic);
        Background = hasAcrylic ? Brushes.Transparent : _isLight ? LightSolidBackground : DarkSolidBackground;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Hide();
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
