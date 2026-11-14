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

    // The full tray from the same real taskbar: icons, network, volume, battery, clock and Show Desktop up to 1920.
    private static readonly ButtonSpan[] CentredTaskbarWithFullTray =
    [
        new(628, 673), new(675, 895), new(897, 941), new(941, 985), new(1249, 1293),
        new(1571, 1603), new(1603, 1635), new(1635, 1667), new(1667, 1711), new(1715, 1739),
        new(1743, 1767), new(1771, 1826), new(1834, 1904), new(1908, 1920),
    ];

    private static WidgetSlot FitAtEnd(double offset, params ButtonSpan[] buttons) =>
        TaskbarFreeSpace.FitAtEnd(offset, 0, 1920, buttons, Spacing, MinWidth);

    [Fact]
    public void AtEnd_UsesGapBeforeTray()
    {
        // Gap between the last app (1293) and the tray (1571), minus spacing: 1301–1563.
        Assert.Equal(new WidgetSlot(1301, 262), FitAtEnd(0, CentredTaskbarWithFullTray));
    }

    [Fact]
    public void AtEnd_OffsetMovesTheEndLeft()
    {
        Assert.Equal(new WidgetSlot(1301, 162), FitAtEnd(100, CentredTaskbarWithFullTray));
    }

    [Fact]
    public void AtEnd_LargeOffset_StopsRightOfTheIcons()
    {
        Assert.Equal(new WidgetSlot(1301, 48), FitAtEnd(1000, CentredTaskbarWithFullTray));
    }

    [Fact]
    public void AtEnd_FreeSpaceAfterTray_IsUsed()
    {
        // Nothing reaches the screen edge: the trailing space counts as the last gap.
        Assert.Equal(new WidgetSlot(1643, 277), FitAtEnd(0, CentredTaskbar));
    }

    [Fact]
    public void AtEnd_LeftAlignedTaskbar_SitsAfterTheApps()
    {
        // Left-aligned: Start and apps from 0 to 400, tray from 1571.
        Assert.Equal(new WidgetSlot(408, 1155), FitAtEnd(0, new ButtonSpan(0, 400), new ButtonSpan(1571, 1920)));
    }

    [Fact]
    public void AtEnd_VerticalTaskbar_FitsCoverCellBetweenAppsAndTray()
    {
        // Real left-docked taskbar (1080 px tall), top→bottom: Start..Settings 259–744, tray 802–1080.
        ButtonSpan[] vertical =
        [
            new(259, 304), new(304, 348), new(348, 392), new(392, 436), new(436, 480), new(480, 524),
            new(524, 568), new(568, 612), new(612, 656), new(656, 700), new(700, 744),
            new(802, 830), new(830, 858), new(858, 886), new(886, 920), new(924, 948), new(952, 976),
            new(980, 1020), new(1028, 1064), new(1068, 1080),
        ];

        // Gap 744–802 minus spacing = 752–794 (42): a 40 px cover cell fits; a 48 px square wouldn't.
        Assert.Equal(new WidgetSlot(752, 40), TaskbarFreeSpace.FitAtEnd(8, 0, 1080, vertical, Spacing, minWidth: 40));
        // The old 48 px square didn't fit there and fell back to the gap above Start (0–251, end 251 - 8).
        Assert.Equal(new WidgetSlot(0, 243), TaskbarFreeSpace.FitAtEnd(8, 0, 1080, vertical, Spacing, minWidth: 48));
    }

    [Fact]
    public void AtEnd_NoGapWithRoom_Hides()
    {
        Assert.Equal(0, FitAtEnd(0, new ButtonSpan(0, 1000), new ButtonSpan(1030, 1920)).Width);
    }
}
