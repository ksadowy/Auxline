using System.Windows;
using System.Windows.Media;

namespace Auxline.Controls;

/// <summary>
/// A value shown as a row of separate blocks (pixel-style progress and volume). The block being filled can be drawn
/// differently (<see cref="ActiveBrush"/>, <see cref="ActiveOpacity"/>), e.g. blinking. Drawn directly: a template
/// with one element per block would be far heavier for the same look.
/// </summary>
public sealed class SegmentBar : RangeBar
{
    /// <summary>Preferred block length; the count is whatever fits, then blocks stretch to fill the width exactly.</summary>
    public static readonly DependencyProperty SegmentLengthProperty = RenderProperty<SegmentBar, double>(nameof(SegmentLength), 14.0);
    public static readonly DependencyProperty GapProperty = RenderProperty<SegmentBar, double>(nameof(Gap), 2.0);
    public static readonly DependencyProperty FilledBrushProperty = RenderProperty<SegmentBar, Brush?>(nameof(FilledBrush), null);
    public static readonly DependencyProperty EmptyBrushProperty = RenderProperty<SegmentBar, Brush?>(nameof(EmptyBrush), null);
    public static readonly DependencyProperty ActiveBrushProperty = RenderProperty<SegmentBar, Brush?>(nameof(ActiveBrush), null);
    public static readonly DependencyProperty ActiveOpacityProperty = RenderProperty<SegmentBar, double>(nameof(ActiveOpacity), 1.0);

    /// <summary>Blocks grow taller from left to right, like a signal meter.</summary>
    public static readonly DependencyProperty IsRisingProperty = RenderProperty<SegmentBar, bool>(nameof(IsRising), false);

    /// <summary>The first block's height relative to the last when <see cref="IsRising"/>.</summary>
    private const double RisingStart = 0.25;

    public double SegmentLength { get => (double)GetValue(SegmentLengthProperty); set => SetValue(SegmentLengthProperty, value); }

    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    public Brush? FilledBrush { get => (Brush?)GetValue(FilledBrushProperty); set => SetValue(FilledBrushProperty, value); }

    public Brush? EmptyBrush { get => (Brush?)GetValue(EmptyBrushProperty); set => SetValue(EmptyBrushProperty, value); }

    /// <summary>The block currently being filled; null = drawn like the empty ones.</summary>
    public Brush? ActiveBrush { get => (Brush?)GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }

    public double ActiveOpacity { get => (double)GetValue(ActiveOpacityProperty); set => SetValue(ActiveOpacityProperty, value); }

    public bool IsRising { get => (bool)GetValue(IsRisingProperty); set => SetValue(IsRisingProperty, value); }

    /// <summary>How many blocks fit, and how many of them are filled.</summary>
    internal static (int Count, int Filled) Layout(double width, double segmentLength, double gap, double fraction)
    {
        var count = Math.Max(1, (int)Math.Floor((width + gap) / (segmentLength + gap)));
        var filled = double.IsFinite(fraction) ? (int)Math.Floor(Math.Clamp(fraction, 0, 1) * count) : 0;
        return (count, filled);
    }

    protected override void OnRender(DrawingContext context)
    {
        var (width, height) = (ActualWidth, ActualHeight);
        if (width <= 0 || height <= 0) return;

        var (count, filled) = Layout(width, SegmentLength, Gap, Fraction);
        var length = (width - Gap * (count - 1)) / count;

        for (var i = 0; i < count; i++)
        {
            // Whole pixels keep the blocks crisp.
            var left = Math.Round(i * (length + Gap));
            var right = Math.Round(i * (length + Gap) + length);
            var blockHeight = IsRising && count > 1 ? Math.Round(height * (RisingStart + (1 - RisingStart) * i / (count - 1))) : height;
            var block = new Rect(left, height - blockHeight, Math.Max(1, right - left), blockHeight);

            context.DrawRectangle(i < filled ? FilledBrush : EmptyBrush, null, block);
            if (i == filled && ActiveBrush is not null)
            {
                context.PushOpacity(ActiveOpacity);
                context.DrawRectangle(ActiveBrush, null, block);
                context.Pop();
            }
        }
    }
}
