using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Auxline.Controls;
using Auxline.Interop;
using Auxline.Models;
using Auxline.Services;
using Auxline.ViewModels;
using Auxline.Views.FlyoutStyles;
using Auxline.Views.Themes;
using static Auxline.Interop.NativeMethods;

namespace Auxline.Views;

/// <summary>
/// Popup next to the widget with cover art, progress, controls and volume, in the chosen layout (FlyoutStyles).
/// Reused: hidden, never closed. Closes when it loses focus (click anywhere else) or on Esc.
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

    /// <summary>Each style names its seek bar this way; the window handles seeking for all of them.</summary>
    private const string ProgressSliderName = "ProgressSlider";

    private LookSettings _look = new();
    private FlyoutThemeTraits _traits = FlyoutThemes.TraitsOf(FlyoutTheme.Default);
    /// <summary>The flyout's effective light/dark: the setting, unless the theme has a fixed palette.</summary>
    private bool _isLight;
    private FlyoutStyle? _appliedStyle;
    private (FlyoutTheme Theme, bool IsLight)? _appliedTheme;
    /// <summary>The current theme's dictionary (merged into the window's resources).</summary>
    private ResourceDictionary _themeResources = new();
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
        SetResourceReference(ForegroundProperty, "FlyoutForegroundBrush");

        SourceInitialized += OnSourceInitialized;
        Deactivated += (_, _) =>
        {
            _hiddenByFocusLossAt = Environment.TickCount64;
            Hide();
        };
        KeyDown += OnKeyDown;
        PreviewMouseLeftButtonDown += OnPreviewMouseDown;
        PreviewMouseLeftButtonUp += OnPreviewMouseUp;
        IsVisibleChanged += (_, _) =>
        {
            Player.SetProgressShown(IsVisible);
            UpdateThemeAnimation();
        };
        DataContextChanged += OnDataContextChanged;
        // Content can grow while open (e.g. the progress bar appears); keep the edge facing the widget in place.
        SizeChanged += (_, e) =>
        {
            if (IsVisible) PlaceNextToAnchor(e.NewSize);
        };
    }

    /// <summary>Applies live; safe to call before the window is shown.</summary>
    /// <param name="isLight">Resolved theme (the "System" preference already replaced by the actual system theme).</param>
    public void ApplyLook(LookSettings look, bool isLight)
    {
        _look = look;
        _traits = FlyoutThemes.TraitsOf(look.FlyoutTheme);
        _isLight = _traits.IsLight ?? isLight;
        FontFamily = _traits.Font ?? new FontFamily(look.FontFamily);

        // Swapping the theme re-applies every template: only when it changes, not on every settings change.
        if (_appliedTheme != (look.FlyoutTheme, _isLight))
        {
            _appliedTheme = (look.FlyoutTheme, _isLight);
            _themeResources = FlyoutThemes.CreateResources(look.FlyoutTheme, _isLight);
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(_themeResources);
            ApplyCoverColors();
        }

        // Recreate the layout only when the style changes: rebuilding resets things like the compact volume toggle.
        if (look.FlyoutStyle != _appliedStyle)
        {
            _appliedStyle = look.FlyoutStyle;
            StyleHost.Content = FlyoutStyleFactory.Create(look.FlyoutStyle);
        }
        if (_handle != IntPtr.Zero) ApplyFrame();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PlayerViewModel oldPlayer) oldPlayer.PropertyChanged -= OnPlayerPropertyChanged;
        if (e.NewValue is PlayerViewModel newPlayer) newPlayer.PropertyChanged += OnPlayerPropertyChanged;
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.CoverColor):
                ApplyCoverColors();
                break;
            case nameof(PlayerViewModel.IsPlaying):
                UpdateThemeAnimation();
                break;
            case nameof(PlayerViewModel.Title) when IsVisible:
                ThemeFrame.RaiseEvent(new RoutedEventArgs(ThemeAnimation.TrackChangedEvent));
                break;
        }
    }

    /// <summary>
    /// The cover-tinted style's colours from the album art, unless the theme has a fixed palette (which defines them).
    /// Written into the theme's dictionary, so switching themes drops them.
    /// </summary>
    private void ApplyCoverColors()
    {
        if (_traits.IsLight is null) FlyoutPalette.ApplyCover(_themeResources, (DataContext as PlayerViewModel)?.CoverColor, _isLight);
    }

    /// <summary>Theme animations loop only while they can be seen and the music plays.</summary>
    private void UpdateThemeAnimation() => ThemeAnimation.SetIsActive(this, IsVisible && Player.IsPlaying);

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
        // Each style sets its own width (Minimalist is narrower).
        var content = (FrameworkElement)Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        PlaceNextToAnchor(content.DesiredSize);

        Show();
        Activate();
    }

    private void PlaceNextToAnchor(Size size)
    {
        var (width, height) = (size.Width, size.Height);
        (Left, Top) = _taskbarEdge switch
        {
            TaskbarEdge.Top => (_anchor.Left, _anchor.Bottom + GapToAnchorDip),
            TaskbarEdge.Left => (_anchor.Right + GapToAnchorDip, _anchor.Top),
            TaskbarEdge.Right => (_anchor.Left - width - GapToAnchorDip, _anchor.Top),
            _ => (_anchor.Left, _anchor.Top - height - GapToAnchorDip),
        };

        // Keep it on the widget's monitor: next to a vertical taskbar it hangs down from the widget, and docked right
        // near the screen edge it would stick out sideways.
        Top = Math.Max(_workArea.Top, Math.Min(Top, _workArea.Bottom - height));
        Left = Math.Max(_workArea.Left, Math.Min(Left, _workArea.Right - width));
    }

    /// <summary>Pressing the seek bar pauses position updates, so they don't fight the thumb.</summary>
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsOnProgressSlider(e.OriginalSource as DependencyObject)) Player.IsSeeking = true;
    }

    /// <summary>Seek once on release (click or end of drag), not on every value change while dragging.</summary>
    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!Player.IsSeeking) return;
        Player.IsSeeking = false;
        Player.SeekCommand.Execute(null);
    }

    private static bool IsOnProgressSlider(DependencyObject? element)
    {
        // Text runs aren't visuals; anything clicked on a slider is.
        while (element is Visual)
        {
            if (element is Slider { Name: ProgressSliderName }) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
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
        var useAcrylic = _traits.UsesSystemBackdrop && _look.UseAcrylic;
        var hasAcrylic = WindowBackdrop.Apply(_handle, isDark: !_isLight, _traits.Corners ?? _look.FlyoutCorners, useAcrylic);
        if (hasAcrylic) Background = Brushes.Transparent;
        else SetResourceReference(BackgroundProperty, "FlyoutBackgroundBrush");
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Hide();
    }
}
