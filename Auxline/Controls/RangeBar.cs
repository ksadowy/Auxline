using System.Windows;

namespace Auxline.Controls;

/// <summary>
/// Base for the drawn progress/volume visuals of the themes (<see cref="SegmentBar"/>, <see cref="PelletBar"/>,
/// <see cref="RulerBar"/>): a value in a range, shown inside a slider template that handles the input.
/// </summary>
public abstract class RangeBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = RenderProperty<RangeBar, double>(nameof(Value), 0.0);
    public static readonly DependencyProperty MinimumProperty = RenderProperty<RangeBar, double>(nameof(Minimum), 0.0);
    public static readonly DependencyProperty MaximumProperty = RenderProperty<RangeBar, double>(nameof(Maximum), 1.0);

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    /// <summary>Where the value is, 0 to 1 (0 for an empty range or invalid values).</summary>
    protected double Fraction
    {
        get
        {
            var range = Maximum - Minimum;
            var fraction = range > 0 ? (Value - Minimum) / range : 0;
            return double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        }
    }

    /// <summary>A dependency property that redraws the bar when it changes.</summary>
    protected static DependencyProperty RenderProperty<TOwner, T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(TOwner),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}
