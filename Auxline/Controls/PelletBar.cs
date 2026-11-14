using System.Windows;
using System.Windows.Media;

namespace Auxline.Controls;

/// <summary>
/// Progress as a row of pellets being eaten (Arcade theme): played ones are tiny and dim, a round marker sits at the
/// position, the ones ahead are bright. <see cref="Phase"/> (0 to 1, looped by the theme while playing) makes the
/// marker pulse and the pellets ahead twinkle; at 0 everything is still.
/// </summary>
public sealed class PelletBar : RangeBar
{
    public static readonly DependencyProperty SpacingProperty = RenderProperty<PelletBar, double>(nameof(Spacing), 18.0);
    public static readonly DependencyProperty EatenBrushProperty = RenderProperty<PelletBar, Brush?>(nameof(EatenBrush), null);
    public static readonly DependencyProperty PelletBrushProperty = RenderProperty<PelletBar, Brush?>(nameof(PelletBrush), null);
    public static readonly DependencyProperty MarkerBrushProperty = RenderProperty<PelletBar, Brush?>(nameof(MarkerBrush), null);
    /// <summary>Drawn behind the marker, twice its size (e.g. a radial gradient fading out).</summary>
    public static readonly DependencyProperty GlowBrushProperty = RenderProperty<PelletBar, Brush?>(nameof(GlowBrush), null);
    public static readonly DependencyProperty PhaseProperty = RenderProperty<PelletBar, double>(nameof(Phase), 0.0);

    private const double EatenSize = 2;
    private const double PelletSize = 4;
    private const double PulseDepth = 0.25;
    /// <summary>Phase offset between neighbouring pellets, so the twinkle runs along the row.</summary>
    private const double TwinkleStep = 0.08;

    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    public Brush? EatenBrush { get => (Brush?)GetValue(EatenBrushProperty); set => SetValue(EatenBrushProperty, value); }

    public Brush? PelletBrush { get => (Brush?)GetValue(PelletBrushProperty); set => SetValue(PelletBrushProperty, value); }

    public Brush? MarkerBrush { get => (Brush?)GetValue(MarkerBrushProperty); set => SetValue(MarkerBrushProperty, value); }

    public Brush? GlowBrush { get => (Brush?)GetValue(GlowBrushProperty); set => SetValue(GlowBrushProperty, value); }

    public double Phase { get => (double)GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }

    protected override void OnRender(DrawingContext context)
    {
        var (width, height) = (ActualWidth, ActualHeight);
        if (width <= 0 || height <= 0) return;

        var count = Math.Max(2, (int)Math.Floor(width / Spacing));
        var marker = Math.Min(count - 1, (int)Math.Floor(Fraction * count));
        var step = width / count;
        var middle = height / 2;
        var isAnimated = Phase > 0;

        for (var i = 0; i < count; i++)
        {
            var center = new Point(Math.Round((i + 0.5) * step), middle);
            if (i < marker)
            {
                context.DrawRectangle(EatenBrush, null, Square(center, EatenSize));
            }
            else if (i == marker)
            {
                // Two pulses per loop.
                var pulse = isAnimated ? 1 - PulseDepth * Math.Pow(Math.Sin(2 * Math.PI * Phase), 2) : 1;
                var radius = height / 2 * pulse;
                if (GlowBrush is not null) context.DrawEllipse(GlowBrush, null, center, radius * 2, radius * 2);
                context.DrawEllipse(MarkerBrush, null, center, radius, radius);
            }
            else
            {
                var opacity = isAnimated ? 0.35 + 0.65 * (0.5 + 0.5 * Math.Cos(2 * Math.PI * (Phase - i * TwinkleStep))) : 1;
                context.PushOpacity(opacity);
                context.DrawRectangle(PelletBrush, null, Square(center, PelletSize));
                context.Pop();
            }
        }
    }

    private static Rect Square(Point center, double size) => new(center.X - size / 2, center.Y - size / 2, size, size);
}
