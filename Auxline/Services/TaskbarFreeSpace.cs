namespace Auxline.Services;

/// <summary>Horizontal extent of a taskbar button, in physical pixels.</summary>
public readonly record struct ButtonSpan(double Left, double Right);

/// <summary>Where the widget goes, in physical pixels. <see cref="Width"/> 0 means there's no room anywhere.</summary>
public readonly record struct WidgetSlot(double Start, double Width);

public static class TaskbarFreeSpace
{
    /// <summary>
    /// Places the widget in the gaps between taskbar buttons so it never covers one. The requested start is used
    /// when it lies in a gap with at least <paramref name="minWidth"/> of room; otherwise the widget moves left to
    /// the nearest gap that has room and stops against its right side (i.e. right before the icons).
    /// </summary>
    /// <param name="spacing">Distance kept from buttons on either side.</param>
    public static WidgetSlot Fit(
        double requestedStart, double taskbarLeft, double taskbarRight,
        IEnumerable<ButtonSpan> buttons, double spacing, double minWidth)
    {
        var gaps = FreeGaps(taskbarLeft, taskbarRight, buttons, spacing);

        // Nearest gap at or left of the requested start first, so the widget only ever moves left.
        for (var i = gaps.Count - 1; i >= 0; i--)
        {
            var (gapStart, gapEnd) = gaps[i];
            if (gapStart > requestedStart) continue;

            var start = Math.Max(gapStart, Math.Min(requestedStart, gapEnd - minWidth));
            if (gapEnd - start >= minWidth) return new WidgetSlot(start, gapEnd - start);
        }
        return new WidgetSlot(requestedStart, 0);
    }

    /// <summary>
    /// "Right of the icons": the last free gap with room (i.e. the one before the tray), with the widget's end
    /// <paramref name="offset"/> before the gap's end, but never so far that less than <paramref name="minWidth"/> remains.
    /// The widget aligns to <c>Start + Width</c> and grows towards the start.
    /// </summary>
    public static WidgetSlot FitAtEnd(
        double offset, double taskbarLeft, double taskbarRight,
        IEnumerable<ButtonSpan> buttons, double spacing, double minWidth)
    {
        var gaps = FreeGaps(taskbarLeft, taskbarRight, buttons, spacing);
        for (var i = gaps.Count - 1; i >= 0; i--)
        {
            var (gapStart, gapEnd) = gaps[i];
            if (gapEnd - gapStart < minWidth) continue; // e.g. the 2 px between tray icons

            var end = Math.Max(gapStart + minWidth, gapEnd - offset);
            return new WidgetSlot(gapStart, end - gapStart);
        }
        return new WidgetSlot(taskbarRight, 0);
    }

    /// <summary>Spaces between buttons, shrunk by <paramref name="spacing"/> next to each button (not at the taskbar's ends).</summary>
    private static List<(double Start, double End)> FreeGaps(
        double taskbarLeft, double taskbarRight, IEnumerable<ButtonSpan> buttons, double spacing)
    {
        var gaps = new List<(double, double)>();
        var cursor = taskbarLeft;
        var isCursorAtButton = false;

        foreach (var button in buttons.OrderBy(b => b.Left))
        {
            if (button.Left > cursor)
            {
                gaps.Add((cursor + (isCursorAtButton ? spacing : 0), button.Left - spacing));
            }
            if (button.Right > cursor)
            {
                cursor = button.Right;
                isCursorAtButton = true;
            }
        }

        if (taskbarRight > cursor) gaps.Add((cursor + (isCursorAtButton ? spacing : 0), taskbarRight));
        return gaps;
    }
}
