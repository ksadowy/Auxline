using System.Windows;
using Auxline.Services;

namespace Auxline.Tests;

public sealed class TaskbarGeometryTests
{
    // The real setup: primary 0–1920, secondary 1920–3840, both 1080 tall.
    private static readonly Int32Rect Primary = new(0, 0, 1920, 1080);
    private static readonly Int32Rect Secondary = new(1920, 0, 1920, 1080);

    [Fact]
    public void BottomTaskbar()
    {
        var placement = TaskbarGeometry.Dock(new Int32Rect(0, 1032, 1920, 48), Primary);

        Assert.Equal(new TaskbarPlacement(new Int32Rect(0, 1032, 1920, 48), TaskbarEdge.Bottom), placement);
    }

    [Fact]
    public void SecondaryMonitorBottomTaskbar()
    {
        var placement = TaskbarGeometry.Dock(new Int32Rect(1920, 1032, 1920, 48), Secondary);

        Assert.Equal(new TaskbarPlacement(new Int32Rect(1920, 1032, 1920, 48), TaskbarEdge.Bottom), placement);
    }

    [Fact]
    public void AutoHiddenBottomTaskbar_DockedBoundsStayOnScreen()
    {
        // Slid down: only 2 px still on screen (measured on Windows 11).
        var placement = TaskbarGeometry.Dock(new Int32Rect(0, 1078, 1920, 48), Primary);

        Assert.Equal(new TaskbarPlacement(new Int32Rect(0, 1032, 1920, 48), TaskbarEdge.Bottom), placement);
    }

    [Fact]
    public void TopTaskbar()
    {
        Assert.Equal(TaskbarEdge.Top, TaskbarGeometry.Dock(new Int32Rect(1920, 0, 1920, 48), Secondary).Edge);
    }

    [Fact]
    public void LeftTaskbar()
    {
        var placement = TaskbarGeometry.Dock(new Int32Rect(0, 0, 48, 1080), Primary);

        Assert.Equal(new TaskbarPlacement(new Int32Rect(0, 0, 48, 1080), TaskbarEdge.Left), placement);
    }

    [Fact]
    public void RightTaskbarOnSecondaryMonitor()
    {
        var placement = TaskbarGeometry.Dock(new Int32Rect(3792, 0, 48, 1080), Secondary);

        Assert.Equal(new TaskbarPlacement(new Int32Rect(3792, 0, 48, 1080), TaskbarEdge.Right), placement);
    }
}
