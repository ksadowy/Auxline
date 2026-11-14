using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Auxline.Controls;

public enum MeterShape
{
    /// <summary>A few bars bouncing with the level, like an equaliser.</summary>
    Bars,

    /// <summary>An analogue VU dial whose needle swings with the level.</summary>
    Needle,
}

/// <summary>How a theme draws the widget's music meter.</summary>
/// <param name="Steps">0 = smooth bar heights; otherwise heights snap to that many levels (pixel looks).</param>
/// <param name="Color">Null = the widget's text colour (which follows the taskbar).</param>
public sealed record MeterLook(MeterShape Shape, int BarCount, double BarWidth, double Gap, int Steps, bool IsRounded, Color? Color);

/// <summary>
/// The widget's music meter: <see cref="Level"/> (0 to 1, the playing app's loudness) drawn in the theme's
/// <see cref="Look"/>. There is one level for the whole app, not frequency bands, so each bar sways at its own
/// speed around it; that reads as an equaliser while honestly following the music's loudness.
/// </summary>
public sealed class LevelMeter : FrameworkElement
{
    // Before the properties: static fields initialise in order, and LookProperty's default is this.
    public static readonly MeterLook DefaultLook = new(MeterShape.Bars, BarCount: 4, BarWidth: 3, Gap: 2, Steps: 0, IsRounded: true, Color: null);

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LookProperty = DependencyProperty.Register(
        nameof(Look), typeof(MeterLook), typeof(LevelMeter),
        new FrameworkPropertyMetadata(DefaultLook, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnLookChanged));

    /// <summary>Inherited text colour, used when the look has no colour of its own.</summary>
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(LevelMeter), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Bar sway speeds in radians per second; different, so the bars never move in step.</summary>
    private static readonly double[] SwaySpeeds = [7.1, 9.3, 5.7, 8.2, 6.4];

    /// <summary>A bar never drops below this share of the level, so loud music keeps every bar up.</summary>
    private const double SwayFloor = 0.45;

    private const double MinBarHeight = 2;
    private const double NeedleWidth = 24;
    /// <summary>The needle sweeps ±this many degrees from straight up.</summary>
    private const double NeedleSweep = 55;

    private Brush? _lookBrush;

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    public MeterLook Look { get => (MeterLook)GetValue(LookProperty); set => SetValue(LookProperty, value); }

    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>Each bar's height as a share of the full height (0 to 1) at the given level and moment.</summary>
    internal static double[] BarHeights(double level, int count, int steps, double seconds)
    {
        var heights = new double[count];
        for (var i = 0; i < count; i++)
        {
            var sway = 0.5 + 0.5 * Math.Sin(seconds * SwaySpeeds[i % SwaySpeeds.Length] + i * 1.7);
            var height = Math.Clamp(level, 0, 1) * (SwayFloor + (1 - SwayFloor) * sway);
            heights[i] = steps > 0 ? Math.Ceiling(height * steps) / steps : height;
        }
        return heights;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var look = Look;
        var width = look.Shape == MeterShape.Needle ? NeedleWidth : look.BarCount * look.BarWidth + (look.BarCount - 1) * look.Gap;
        return new Size(width, double.IsFinite(availableSize.Height) ? availableSize.Height : 0);
    }

    protected override void OnRender(DrawingContext context)
    {
        var brush = _lookBrush ?? Foreground;
        if (ActualHeight <= 0) return;
        if (Look.Shape == MeterShape.Needle) DrawNeedle(context, brush);
        else DrawBars(context, brush);
    }

    private void DrawBars(DrawingContext context, Brush brush)
    {
        var look = Look;
        var heights = BarHeights(Level, look.BarCount, look.Steps, Environment.TickCount64 / 1000.0);
        var radius = look.IsRounded ? look.BarWidth / 2 : 0;
        for (var i = 0; i < heights.Length; i++)
        {
            var height = Math.Max(MinBarHeight, Math.Round(heights[i] * ActualHeight));
            var bar = new Rect(i * (look.BarWidth + look.Gap), ActualHeight - height, look.BarWidth, height);
            context.DrawRoundedRectangle(brush, null, bar, radius, radius);
        }
    }

    /// <summary>A half dial standing on the bottom edge, with a needle from its centre.</summary>
    private void DrawNeedle(DrawingContext context, Brush brush)
    {
        var center = new Point(NeedleWidth / 2, ActualHeight - 1);
        var radius = Math.Min(NeedleWidth / 2, ActualHeight) - 1.5;
        var dial = new Pen(brush, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        context.PushOpacity(0.5);
        context.DrawGeometry(null, dial, Arc(center, radius, -NeedleSweep - 10, NeedleSweep + 10));
        context.Pop();

        var angle = (-NeedleSweep + 2 * NeedleSweep * Math.Clamp(Level, 0, 1)) * Math.PI / 180;
        var tip = new Point(center.X + Math.Sin(angle) * (radius - 1), center.Y - Math.Cos(angle) * (radius - 1));
        context.DrawLine(dial, center, tip);
        context.DrawEllipse(brush, null, center, 1.5, 1.5);
    }

    /// <summary>Arc between two angles measured clockwise from straight up.</summary>
    private static Geometry Arc(Point center, double radius, double fromDegrees, double toDegrees)
    {
        Point At(double degrees) => new(
            center.X + Math.Sin(degrees * Math.PI / 180) * radius,
            center.Y - Math.Cos(degrees * Math.PI / 180) * radius);

        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(At(fromDegrees), isFilled: false, isClosed: false);
            figure.ArcTo(At(toDegrees), new Size(radius, radius), 0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static void OnLookChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        var meter = (LevelMeter)element;
        meter._lookBrush = ((MeterLook)e.NewValue).Color is { } color ? Frozen(new SolidColorBrush(color)) : null;
    }

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
