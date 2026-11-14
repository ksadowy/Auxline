using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Auxline.Models;
using Auxline.Services;
using static Auxline.Interop.NativeMethods;

namespace Auxline.Views;

/// <summary>
/// Compact now-playing widget drawn on the taskbar, in a free gap between taskbar icons
/// (left side of a horizontal taskbar; cover only on a vertical one).
/// Not embedded in Explorer: a separate window kept on the taskbar by <see cref="TaskbarAttachment"/>.
/// </summary>
public partial class WidgetWindow : Window
{
    /// <summary>Minimum space between the cover and the taskbar's long edges, so it never touches them.</summary>
    private const double CoverMarginDip = 4;

    /// <summary>Space kept between the widget and taskbar buttons.</summary>
    private const double ButtonGapDip = 8;

    /// <summary>Below this the widget can't show a cover and readable text, so it hides instead.</summary>
    private const double MinUsefulWidthDip = 48;

    /// <summary>Re-layouts after moving/realigning the taskbar leave its buttons unreadable for ~1 s.</summary>
    private const int MaxMeasureRetries = 5;
    private static readonly TimeSpan MeasureRetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly TaskbarService _taskbar;
    private readonly TaskbarAttachment _attachment;
    private IntPtr _handle;
    private double _offsetDip = new LayoutSettings().LeftOffset;
    private double _maxWidthSettingDip = new LayoutSettings().MaxWidgetWidth;
    private WidgetAnchor _anchor = new LayoutSettings().Anchor;

    /// <summary>
    /// Last measured taskbar buttons (null until the first measurement succeeds). Placement uses this synchronously,
    /// so moving the widget never waits for (or races) the slow UI Automation query.
    /// </summary>
    private IReadOnlyList<Rect>? _buttons;

    /// <summary>Only the newest measurement may apply; older ones finishing late are stale.</summary>
    private int _latestMeasureRequest;

    public event EventHandler? Clicked;

    /// <summary>Current bounds in DIPs.</summary>
    public Rect Bounds => new(Left, Top, ActualWidth, ActualHeight);

    /// <summary>Which screen edge the taskbar is on, so the flyout opens towards the screen's centre.</summary>
    public TaskbarEdge TaskbarEdge { get; private set; } = TaskbarEdge.Bottom;

    /// <summary>Usable area of the widget's monitor in DIPs, so the flyout stays on that monitor.</summary>
    public Rect MonitorWorkArea { get; private set; } = SystemParameters.WorkArea;

    public WidgetWindow(TaskbarService taskbar)
    {
        InitializeComponent();
        _taskbar = taskbar;

        // Created first so its handle setup (ownership, styles) runs before our own SourceInitialized handler.
        _attachment = new TaskbarAttachment(this, taskbar);
        _attachment.LayoutMayHaveChanged += (_, _) =>
        {
            RepositionAfterLayout();
            MeasureTaskbar();
        };
        _attachment.ShouldHideChanged += (_, shouldHide) => SetHidden(shouldHide);

        SourceInitialized += OnSourceInitialized;
        MouseLeftButtonUp += (_, _) => Clicked?.Invoke(this, EventArgs.Empty);
        DpiChanged += (_, _) => RepositionAfterLayout();
        // Docked right, the right edge stays put: a new title changes the width, so the left edge must move.
        SizeChanged += (_, e) =>
        {
            if (_anchor == WidgetAnchor.RightOfIcons && e.WidthChanged && _handle != IntPtr.Zero) RepositionAfterLayout();
        };
        Closed += (_, _) => _attachment.Dispose();
    }

    /// <summary>Applies live; safe to call before the window is shown.</summary>
    public void ApplyLayout(LayoutSettings layout)
    {
        _offsetDip = layout.LeftOffset;
        _maxWidthSettingDip = layout.MaxWidgetWidth;
        _anchor = layout.Anchor;
        _attachment.SelectMonitor(layout.MonitorDevice);
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
        Reposition();
        MeasureTaskbar();
    }

    /// <summary>Fullscreen app or auto-hidden taskbar (see <see cref="TaskbarAttachment.ShouldHide"/>).</summary>
    private void SetHidden(bool isHidden)
    {
        if (isHidden)
        {
            Hide();
            return;
        }
        Show();
        _attachment.RaiseAboveTaskbar();
    }

    /// <summary>Defer until WPF has applied its own DPI/display handling, which would override our position.</summary>
    private void RepositionAfterLayout() => Dispatcher.BeginInvoke(Reposition, DispatcherPriority.Background);

    private void Reposition()
    {
        if (_attachment.CurrentTaskbar is not { } taskbarInfo)
        {
            Trace.TraceWarning("Taskbar not found; widget position unchanged.");
            return;
        }

        var placement = taskbarInfo.Placement;
        TaskbarEdge = placement.Edge;
        var taskbar = placement.Bounds;
        var isVertical = placement.IsVertical;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var workArea = taskbarInfo.WorkArea;
        MonitorWorkArea = new Rect(workArea.X / scale, workArea.Y / scale, workArea.Width / scale, workArea.Height / scale);

        // Thickness = the taskbar's short side. The cover-size setting is in DIPs, but the taskbar's DIP thickness
        // shrinks with scaling (48 px at 125% ≈ 38 DIP), so the cover is capped to fit.
        var thicknessDip = (isVertical ? taskbar.Width : taskbar.Height) / scale;
        CoverImage.MaxHeight = CoverImage.MaxWidth = Math.Max(0, thicknessDip - CoverMarginDip * 2);
        // On a vertical taskbar the widget is just the cover: as wide as the taskbar, but only as tall as the cover
        // plus margins, so it also fits the narrower gaps (e.g. between the last app and the tray).
        var coverCellDip = Math.Min(CoverImage.Width, CoverImage.MaxHeight) + CoverMarginDip * 2;

        // Placement runs along the taskbar: left→right, or top→bottom for a vertical one. Never cover taskbar icons:
        // stay in a free gap. Docked left, the offset counts from the taskbar's start and the widget grows towards the
        // end; docked right, it sits in the gap before the tray, the offset counts from there and it grows backwards.
        var (axisStart, axisEnd) = isVertical ? (taskbar.Y, taskbar.Y + taskbar.Height) : (taskbar.X, taskbar.X + taskbar.Width);
        var offset = _offsetDip * scale;
        var isDockedAtEnd = _anchor == WidgetAnchor.RightOfIcons;
        var minLength = (isVertical ? coverCellDip : MinUsefulWidthDip) * scale;
        // Right after the taskbar moves (e.g. top → right), the last measurement still describes the old position;
        // read along the new axis it can look like "no room". Treat it as unknown until the fresh one arrives.
        var buttons = _buttons is not null && AreWithin(_buttons, taskbar) ? _buttons : null;
        var slot = (buttons, isDockedAtEnd) switch
        {
            // Layout unknown, docked left: the area from the taskbar's start is usually free; size settings apply.
            (null, false) => new WidgetSlot(axisStart + offset, double.PositiveInfinity),
            // Layout unknown, docked right: the far end is the tray, so wait (hidden) instead of covering it.
            (null, true) => new WidgetSlot(axisEnd, 0),
            (_, false) => TaskbarFreeSpace.Fit(axisStart + offset, axisStart, axisEnd, ToSpans(buttons, isVertical), ButtonGapDip * scale, minLength),
            (_, true) => TaskbarFreeSpace.FitAtEnd(offset, axisStart, axisEnd, ToSpans(buttons, isVertical), ButtonGapDip * scale, minLength),
        };
        var hasRoom = slot.Width > 0;
        var slotEnd = slot.Start + slot.Width;

        if (isVertical)
        {
            ApplyVerticalLayout(thicknessDip, hasRoom ? coverCellDip : 0);
            var y = isDockedAtEnd ? slotEnd - coverCellDip * scale : slot.Start;
            SetWindowPos(_handle, HWND_TOPMOST, taskbar.X, (int)Math.Round(y), 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }
        else
        {
            ApplyHorizontalLayout(thicknessDip, hasRoom ? Math.Min(_maxWidthSettingDip, slot.Width / scale) : 0);
            // Docked right, the left edge depends on the content's width: lay out now to know it.
            if (isDockedAtEnd) UpdateLayout();
            var x = isDockedAtEnd ? slotEnd - ActualWidth * scale : slot.Start;
            SetWindowPos(_handle, HWND_TOPMOST, (int)Math.Round(x), taskbar.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    /// <summary>Cover + title/artist, sized to its content up to <paramref name="maxWidthDip"/> (0 hides it).</summary>
    private void ApplyHorizontalLayout(double thicknessDip, double maxWidthDip)
    {
        TextPanel.Visibility = Visibility.Visible;
        ContentBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
        SizeToContent = SizeToContent.Width;
        MaxHeight = double.PositiveInfinity;
        Height = thicknessDip;
        MaxWidth = maxWidthDip;
    }

    /// <summary>Left/right taskbar: no room for text, so a cell with just the cover (zero height hides it).</summary>
    private void ApplyVerticalLayout(double thicknessDip, double heightDip)
    {
        TextPanel.Visibility = Visibility.Collapsed;
        ContentBorder.HorizontalAlignment = HorizontalAlignment.Center;
        SizeToContent = SizeToContent.Manual;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        Width = thicknessDip;
        // Hide via Height itself, not MaxHeight: lifting a MaxHeight of 0 doesn't regrow the window when Height
        // didn't change, so it stayed a 2 px line.
        Height = heightDip;
    }

    /// <summary>All button centres inside the taskbar, i.e. measured at its current position.</summary>
    private static bool AreWithin(IReadOnlyList<Rect> buttons, Int32Rect taskbar)
    {
        var bounds = new Rect(taskbar.X, taskbar.Y, taskbar.Width, taskbar.Height);
        return buttons.All(button => bounds.Contains(new Point(button.X + button.Width / 2, button.Y + button.Height / 2)));
    }

    /// <summary>Button extents along the taskbar's long axis.</summary>
    private static IEnumerable<ButtonSpan> ToSpans(IReadOnlyList<Rect> buttons, bool isVertical) =>
        buttons.Select(b => isVertical ? new ButtonSpan(b.Top, b.Bottom) : new ButtonSpan(b.Left, b.Right));

    /// <summary>
    /// Re-reads taskbar button positions off the UI thread, then re-places the widget with them. An unknown result
    /// (taskbar context menu open, taskbar re-laying out) keeps the last layout and is retried a few times.
    /// </summary>
    private async void MeasureTaskbar()
    {
        var request = ++_latestMeasureRequest;
        for (var attempt = 0; ; attempt++)
        {
            var taskbarWindow = _attachment.CurrentTaskbar?.Window ?? IntPtr.Zero;
            var buttons = await Task.Run(() => _taskbar.GetButtonBounds(taskbarWindow));
            if (request != _latestMeasureRequest) return;

            if (buttons is not null) _buttons = buttons;
            Reposition();
            if (buttons is not null || attempt >= MaxMeasureRetries) return;

            await Task.Delay(MeasureRetryDelay);
            if (request != _latestMeasureRequest) return;
        }
    }
}
