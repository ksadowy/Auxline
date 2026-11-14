using System.Windows;
using System.Windows.Media;

namespace Auxline.Controls;

/// <summary>
/// A value shown as a row of separate blocks (pixel-style progress and volume). The block being filled can be drawn
/// differently (<see cref="ActiveBrush"/>, <see cref="ActiveOpacity"/>), e.g. blinking. Drawn directly: a template
/// with one element per block would be far heavier for the same look.
/// </summary>
public sealed class SegmentBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = RenderProperty(nameof(Value), 0.0);
    public static readonly DependencyProperty MinimumProperty = RenderProperty(nameof(Minimum), 0.0);
    public static readonly DependencyProperty MaximumProperty = RenderProperty(nameof(Maximum), 1.0);

    /// <summary>Preferred block length; the count is whatever fits, then blocks stretch to fill the width exactly.</summary>
    public static readonly DependencyProperty SegmentLengthProperty = RenderProperty(nameof(SegmentLength), 14.0);
    public static readonly DependencyProperty GapProperty = RenderProperty(nameof(Gap), 2.0);
    public static readonly DependencyProperty FilledBrushProperty = RenderProperty<Brush?>(nameof(FilledBrush), null);
    public static readonly DependencyProperty EmptyBrushProperty = RenderProperty<Brush?>(nameof(EmptyBrush), null);
    public static readonly DependencyProperty ActiveBrushProperty = RenderProperty<Brush?>(nameof(ActiveBrush), null);
    public static readonly DependencyProperty ActiveOpacityProperty = RenderProperty(nameof(ActiveOpacity), 1.0);

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public double SegmentLength { get => (double)GetValue(SegmentLengthProperty); set => SetValue(SegmentLengthProperty, value); }

    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    public Brush? FilledBrush { get => (Brush?)GetValue(FilledBrushProperty); set => SetValue(FilledBrushProperty, value); }

    public Brush? EmptyBrush { get => (Brush?)GetValue(EmptyBrushProperty); set => SetValue(EmptyBrushProperty, value); }

    /// <summary>The block currently being filled; null = drawn like the empty ones.</summary>
    public Brush? ActiveBrush { get => (Brush?)GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }

    public double ActiveOpacity { get => (double)GetValue(ActiveOpacityProperty); set => SetValue(ActiveOpacityProperty, value); }

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

        var range = Maximum - Minimum;
        var fraction = range > 0 ? (Value - Minimum) / range : 0;
        var (count, filled) = Layout(width, SegmentLength, Gap, fraction);
        var length = (width - Gap * (count - 1)) / count;

        for (var i = 0; i < count; i++)
        {
            // Whole pixels keep the blocks crisp.
            var left = Math.Round(i * (length + Gap));
            var right = Math.Round(i * (length + Gap) + length);
            var block = new Rect(left, 0, Math.Max(1, right - left), height);

            context.DrawRectangle(i < filled ? FilledBrush : EmptyBrush, null, block);
            if (i == filled && ActiveBrush is not null)
            {
                context.PushOpacity(ActiveOpacity);
                context.DrawRectangle(ActiveBrush, null, block);
                context.Pop();
            }
        }
    }

    private static DependencyProperty RenderProperty<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(SegmentBar),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}
