using System.Windows;
using System.Windows.Media;

namespace Auxline.Controls;

/// <summary>
/// A value shown as a ruler (Orbit theme): thin evenly spaced ticks, longer every 4th and 8th, lit up to the value,
/// with an optional triangle marker above the position.
/// </summary>
public sealed class RulerBar : RangeBar
{
    public static readonly DependencyProperty SpacingProperty = RenderProperty<RulerBar, double>(nameof(Spacing), 7.0);
    public static readonly DependencyProperty LitBrushProperty = RenderProperty<RulerBar, Brush?>(nameof(LitBrush), null);
    public static readonly DependencyProperty DimBrushProperty = RenderProperty<RulerBar, Brush?>(nameof(DimBrush), null);
    public static readonly DependencyProperty ShowsMarkerProperty = RenderProperty<RulerBar, bool>(nameof(ShowsMarker), false);

    private const double MarkerWidth = 9;
    private const double MarkerHeight = 6;
    private const double MarkerGap = 2;

    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    public Brush? LitBrush { get => (Brush?)GetValue(LitBrushProperty); set => SetValue(LitBrushProperty, value); }

    public Brush? DimBrush { get => (Brush?)GetValue(DimBrushProperty); set => SetValue(DimBrushProperty, value); }

    public bool ShowsMarker { get => (bool)GetValue(ShowsMarkerProperty); set => SetValue(ShowsMarkerProperty, value); }

    protected override void OnRender(DrawingContext context)
    {
        var (width, height) = (ActualWidth, ActualHeight);
        if (width <= 1 || height <= 0) return;

        var top = ShowsMarker ? MarkerHeight + MarkerGap : 0;
        var tickArea = height - top;
        var intervals = Math.Max(1, (int)Math.Floor((width - 1) / Spacing));
        var fraction = Fraction;

        for (var i = 0; i <= intervals; i++)
        {
            var relativeLength = i % 8 == 0 ? 1 : i % 4 == 0 ? 0.65 : 0.4;
            var tickHeight = Math.Round(tickArea * relativeLength);
            var x = Math.Round(i * (width - 1) / intervals);
            var brush = (double)i / intervals <= fraction ? LitBrush : DimBrush;
            context.DrawRectangle(brush, null, new Rect(x, height - tickHeight, 1, tickHeight));
        }

        if (!ShowsMarker) return;
        var markerX = Math.Round(fraction * (width - 1)) + 0.5;
        var triangle = new StreamGeometry();
        using (var figure = triangle.Open())
        {
            figure.BeginFigure(new Point(markerX - MarkerWidth / 2, 0), isFilled: true, isClosed: true);
            figure.LineTo(new Point(markerX + MarkerWidth / 2, 0), isStroked: false, isSmoothJoin: false);
            figure.LineTo(new Point(markerX, MarkerHeight), isStroked: false, isSmoothJoin: false);
        }
        triangle.Freeze();
        context.DrawGeometry(LitBrush, null, triangle);
    }
}
