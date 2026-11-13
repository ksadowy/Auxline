using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Auxline.Interop;
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
    private const double GapAboveAnchorDip = 12;

    private readonly bool _isLightTheme;
    private Rect _anchor;

    private PlayerViewModel Player => (PlayerViewModel)DataContext;

    public FlyoutWindow(TaskbarService taskbar)
    {
        InitializeComponent();
        _isLightTheme = taskbar.UsesLightTheme();
        Foreground = _isLightTheme ? Brushes.Black : Brushes.White;

        SourceInitialized += OnSourceInitialized;
        Deactivated += (_, _) => Hide();
        KeyDown += OnKeyDown;
        IsVisibleChanged += (_, _) => Player.SetProgressShown(IsVisible);
        // Content can grow while open (e.g. the progress bar appears); keep the bottom edge above the widget.
        SizeChanged += (_, e) =>
        {
            if (IsVisible && e.HeightChanged) PlaceAboveAnchor(ActualHeight);
        };
    }

    /// <param name="anchor">Widget bounds in DIPs; the flyout opens above it, left-aligned.</param>
    public void Toggle(Rect anchor)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        // Measure before showing so the flyout appears at its final position, without a jump.
        _anchor = anchor;
        var content = (FrameworkElement)Content;
        content.Measure(new Size(Width, double.PositiveInfinity));
        PlaceAboveAnchor(content.DesiredSize.Height);

        Show();
        Activate();
    }

    private void PlaceAboveAnchor(double height)
    {
        Left = _anchor.Left;
        Top = _anchor.Top - height - GapAboveAnchorDip;
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
        var handle = new WindowInteropHelper(this).Handle;

        var exStyle = GetWindowLongPtr(handle, GWL_EXSTYLE);
        SetWindowLongPtr(handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

        // Without a system menu DWM draws no caption buttons (the red X) on the extended frame.
        var style = GetWindowLongPtr(handle, GWL_STYLE);
        SetWindowLongPtr(handle, GWL_STYLE, style & ~(nint)WS_SYSMENU);

        if (WindowBackdrop.TryApplyAcrylic(handle, isDark: !_isLightTheme))
        {
            // WPF must paint nothing so the DWM backdrop shows through.
            HwndSource.FromHwnd(handle).CompositionTarget.BackgroundColor = Colors.Transparent;
        }
        else
        {
            // Solid colors matching the Windows 11 flyout tints.
            Background = new SolidColorBrush(_isLightTheme ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20));
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Hide();
    }
}
