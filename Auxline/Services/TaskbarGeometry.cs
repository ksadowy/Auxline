using System.Windows;

namespace Auxline.Services;

public static class TaskbarGeometry
{
    /// <summary>
    /// Where a taskbar is docked, from its window and its monitor. Works for secondary taskbars (which the app bar
    /// query doesn't cover) and while an auto-hidden taskbar is slid (mostly) off-screen: the side its centre is on
    /// gives the edge, and the docked bounds are that edge of the monitor.
    /// </summary>
    public static TaskbarPlacement Dock(Int32Rect window, Int32Rect monitor)
    {
        var centreX = window.X + window.Width / 2.0;
        var centreY = window.Y + window.Height / 2.0;

        if (window.Width >= window.Height)
        {
            var isTop = centreY < monitor.Y + monitor.Height / 2.0;
            var y = isTop ? monitor.Y : monitor.Y + monitor.Height - window.Height;
            return new TaskbarPlacement(new Int32Rect(monitor.X, y, monitor.Width, window.Height), isTop ? TaskbarEdge.Top : TaskbarEdge.Bottom);
        }

        var isLeft = centreX < monitor.X + monitor.Width / 2.0;
        var x = isLeft ? monitor.X : monitor.X + monitor.Width - window.Width;
        return new TaskbarPlacement(new Int32Rect(x, monitor.Y, window.Width, monitor.Height), isLeft ? TaskbarEdge.Left : TaskbarEdge.Right);
    }
}
