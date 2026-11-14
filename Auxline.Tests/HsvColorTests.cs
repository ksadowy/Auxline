using Auxline.Controls;

namespace Auxline.Tests;

public sealed class HsvColorTests
{
    [Theory]
    [InlineData(0, 1, 1, 255, 0, 0)]
    [InlineData(120, 1, 1, 0, 255, 0)]
    [InlineData(240, 1, 1, 0, 0, 255)]
    [InlineData(60, 1, 1, 255, 255, 0)]
    [InlineData(0, 0, 1, 255, 255, 255)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(0, 0, 0.5, 128, 128, 128)]
    [InlineData(30, 1, 1, 255, 128, 0)]
    public void ToRgb_KnownColors(double hue, double saturation, double value, byte red, byte green, byte blue)
    {
        Assert.Equal((red, green, blue), new HsvColor(hue, saturation, value).ToRgb());
    }

    [Fact]
    public void ToRgb_HueWrapsAround()
    {
        Assert.Equal(new HsvColor(0, 1, 1).ToRgb(), new HsvColor(360, 1, 1).ToRgb());
        Assert.Equal(new HsvColor(300, 1, 1).ToRgb(), new HsvColor(-60, 1, 1).ToRgb());
    }

    [Theory]
    [InlineData(255, 136, 0)]
    [InlineData(18, 52, 86)]
    [InlineData(200, 200, 200)]
    [InlineData(0, 0, 0)]
    [InlineData(255, 0, 128)]
    public void FromRgbThenToRgb_RoundTrips(byte red, byte green, byte blue)
    {
        Assert.Equal((red, green, blue), HsvColor.FromRgb(red, green, blue).ToRgb());
    }
}
