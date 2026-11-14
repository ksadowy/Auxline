using Auxline.Controls;
using Auxline.Models;

namespace Auxline.Tests;

public sealed class MusicMeterTests
{
    [Fact]
    public void Ballistics_JumpsUpAtOnce()
    {
        Assert.Equal(0.5, MeterBallistics.Next(current: 0.1, peak: 0.25), precision: 6); // √0.25
    }

    [Fact]
    public void Ballistics_FallsBackGradually()
    {
        Assert.Equal(0.9 - MeterBallistics.FallPerReading, MeterBallistics.Next(current: 0.9, peak: 0), precision: 6);
    }

    [Fact]
    public void Ballistics_NeverFallsBelowTheNewPeak()
    {
        Assert.Equal(0.8, MeterBallistics.Next(current: 0.82, peak: 0.64), precision: 6); // √0.64, less than one step down
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    public void Ballistics_InvalidPeak_CountsAsSilence(double peak)
    {
        Assert.Equal(0.5 - MeterBallistics.FallPerReading, MeterBallistics.Next(current: 0.5, peak), precision: 6);
    }

    [Fact]
    public void Ballistics_LoudPeak_CapsAtFull()
    {
        Assert.Equal(1, MeterBallistics.Next(current: 0, peak: 1.7));
    }

    [Fact]
    public void BarHeights_Silence_AllZero()
    {
        Assert.All(LevelMeter.BarHeights(level: 0, count: 5, steps: 0, seconds: 12.3), height => Assert.Equal(0, height));
    }

    [Fact]
    public void BarHeights_FullLevel_SwayWithinTheFloorAndTheTop()
    {
        var heights = Enumerable.Range(0, 50).SelectMany(tick => LevelMeter.BarHeights(level: 1, count: 4, steps: 0, seconds: tick * 0.05));

        Assert.All(heights, height => Assert.InRange(height, 0.45, 1));
    }

    [Fact]
    public void BarHeights_Steps_SnapToWholeLevels()
    {
        var heights = LevelMeter.BarHeights(level: 0.7, count: 4, steps: 4, seconds: 3.3);

        Assert.All(heights, height => Assert.Equal(0, height * 4 % 1, precision: 9));
    }
}
