namespace Auxline.Models;

/// <summary>
/// How the widget's music meter follows the playing app's peak level: it jumps up at once and falls back gradually,
/// like a real level meter, so it reads as movement rather than flicker.
/// </summary>
public static class MeterBallistics
{
    /// <summary>Per 50 ms reading: from full to silent in about 0.8 s.</summary>
    public const double FallPerReading = 0.06;

    /// <param name="current">The meter's level now, 0..1.</param>
    /// <param name="peak">The latest peak reading, 0..1 (linear amplitude).</param>
    public static double Next(double current, double peak)
    {
        // Square root: quiet passages, or a low app volume, still move the meter visibly.
        var target = double.IsFinite(peak) ? Math.Sqrt(Math.Clamp(peak, 0, 1)) : 0;
        return target >= current ? target : Math.Max(target, current - FallPerReading);
    }
}
