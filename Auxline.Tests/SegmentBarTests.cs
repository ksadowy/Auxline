using Auxline.Controls;

namespace Auxline.Tests;

public sealed class SegmentBarTests
{
    [Theory]
    [InlineData(282, 14, 2, 0.36, 17, 6)]  // 17 blocks of 16 px (block + gap) fit, a third of them filled
    [InlineData(282, 14, 2, 1.0, 17, 17)]
    [InlineData(282, 14, 2, 1.5, 17, 17)]  // past the end (position updates can overshoot the duration)
    [InlineData(282, 14, 2, -0.2, 17, 0)]
    [InlineData(282, 14, 2, double.NaN, 17, 0)]
    [InlineData(5, 14, 2, 0.5, 1, 0)]      // narrower than one block: still one
    public void Layout_FitsBlocksAndCountsFilledOnes(double width, double length, double gap, double fraction, int expectedCount, int expectedFilled)
    {
        Assert.Equal((expectedCount, expectedFilled), SegmentBar.Layout(width, length, gap, fraction));
    }
}
