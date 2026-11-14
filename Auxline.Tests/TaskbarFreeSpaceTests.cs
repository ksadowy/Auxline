using Auxline.Services;

namespace Auxline.Tests;

public sealed class TaskbarFreeSpaceTests
{
    private const double Spacing = 8;
    private const double MinWidth = 48;

    // Shapes from a real centred Windows 11 taskbar (1920 px): Start at 628, apps up to 1293, tray icons from 1571.
    private static readonly ButtonSpan[] CentredTaskbar =
    [
        new(628, 673), new(675, 895), new(897, 941), new(941, 985), new(1249, 1293), new(1571, 1603), new(1603, 1635),
    ];

    private static WidgetSlot Fit(double requestedStart, params ButtonSpan[] buttons) =>
        TaskbarFreeSpace.Fit(requestedStart, 0, 1920, buttons, Spacing, MinWidth);

    [Fact]
    public void StartInFreeArea_StaysAndReachesNextButton()
    {
        Assert.Equal(new WidgetSlot(8, 612), Fit(8, CentredTaskbar));
    }

    [Fact]
    public void StartTooCloseToButton_MovesLeftToKeepMinimumWidth()
    {
        Assert.Equal(new WidgetSlot(572, 48), Fit(600, CentredTaskbar));
    }

    [Fact]
    public void StartOnIcons_StopsBeforeTheIcons()
    {
        Assert.Equal(new WidgetSlot(572, 48), Fit(950, CentredTaskbar));
    }

    [Fact]
    public void StartInGapBetweenAppsAndTray_IsAllowed()
    {
        // Free gap 1293–1571 minus spacing on both sides: 1301–1563.
        Assert.Equal(new WidgetSlot(1400, 163), Fit(1400, CentredTaskbar));
    }

    [Fact]
    public void StartOnTrayIcon_MovesIntoGapBeforeTray()
    {
        Assert.Equal(new WidgetSlot(1515, 48), Fit(1610, CentredTaskbar));
    }

    [Fact]
    public void TinyGapsBetweenIcons_AreSkipped()
    {
        // 673–675 is only 2 px wide: no room, so the widget stops before Start instead.
        Assert.Equal(new WidgetSlot(572, 48), Fit(674, CentredTaskbar));
    }

    [Fact]
    public void LeftAlignedTaskbarWithNoRoomBefore_UsesNextGapToTheLeftOnly()
    {
        // Icons start at the left edge: nothing free at or left of the requested start.
        Assert.Equal(0, Fit(10, new ButtonSpan(0, 400)).Width);
    }

    [Fact]
    public void NoButtons_WholeRemainderIsFree()
    {
        Assert.Equal(new WidgetSlot(8, 1912), Fit(8));
    }
}
