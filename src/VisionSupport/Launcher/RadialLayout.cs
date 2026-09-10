using System.Windows;

namespace VisionSupport.Launcher;

/// <summary>
/// Where each item of the radial menu sits, relative to the launcher icon's centre.
///
/// Split out from the window because it is the one part of the menu that has a right answer;
/// everything else about the menu - timing, easing, tile size - is taste, and taste does not
/// belong in a unit test.
/// </summary>
public static class RadialLayout
{
    /// <summary>
    /// Offsets from the centre, in device-independent pixels, for <paramref name="count"/> items
    /// on a circle of <paramref name="radius"/>. Item 0 sits at twelve o'clock and the rest
    /// follow clockwise. An empty array for a count of zero or less.
    /// </summary>
    public static Point[] Offsets(int count, double radius)
    {
        if (count <= 0) return Array.Empty<Point>();

        var offsets = new Point[count];
        double step = 2 * Math.PI / count;

        for (int i = 0; i < count; i++)
        {
            // -PI/2 puts item 0 at twelve o'clock. Y grows downward in WPF, so adding the step
            // walks clockwise on screen without negating anything.
            double angle = -Math.PI / 2 + i * step;
            offsets[i] = new Point(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }

        return offsets;
    }
}
