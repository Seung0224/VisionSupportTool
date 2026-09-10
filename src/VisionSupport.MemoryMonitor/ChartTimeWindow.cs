namespace MemMon;

/// <summary>
/// Pure geometry for the monitor chart's time axis: a window of a chosen width that either
/// follows the newest sample (newest pinned to the right edge, the trace scrolling left under
/// it) or sits where the horizontal scrollbar left it.
///
/// Split out of <see cref="MonitorView"/> so the edge cases - a session shorter than the
/// window, a frozen window the buffer is scrolling out from under - can be tested without a
/// live plot.
/// </summary>
public static class ChartTimeWindow
{
    /// <summary>Window width the chart opens with, in seconds. One of the presets (5/10/30/60 min).</summary>
    public const double DefaultSpanSeconds = 1800; // 30 minutes

    /// <summary>Floor on the drawn axis width, so the first few seconds of a session still have an axis.</summary>
    private const double MinWidthSeconds = 60;

    /// <summary>
    /// The [start, end] elapsed-seconds the chart shows.
    /// <list type="bullet">
    ///   <item>following live: the newest sample sits on the right edge; the window is
    ///   <paramref name="spanSeconds"/> wide once that much history exists, and just as wide as
    ///   the history until then (so the trace still enters from the right, without a left gap).</item>
    ///   <item>otherwise: a <paramref name="spanSeconds"/>-wide window starting at
    ///   <paramref name="desiredStart"/>, clamped so it neither begins before the retained
    ///   buffer nor ends past the newest sample.</item>
    /// </list>
    /// </summary>
    public static (double Start, double End) View(
        double bufferStart, double latest, double spanSeconds, bool followLive, double desiredStart)
    {
        if (followLive)
        {
            double start = Math.Max(bufferStart, latest - spanSeconds);
            if (latest - start < MinWidthSeconds) start = latest - MinWidthSeconds;
            return (start, latest);
        }

        double maxStart = Math.Max(bufferStart, latest - spanSeconds);
        double frozenStart = Math.Clamp(desiredStart, bufferStart, maxStart);
        return (frozenStart, frozenStart + spanSeconds);
    }

    /// <summary>
    /// Horizontal-scrollbar geometry for panning a <paramref name="spanSeconds"/>-wide window
    /// across <c>[bufferStart, latest]</c>: the left edge slides over <c>[Min, Max]</c> and the
    /// thumb covers <c>Viewport</c> of it. <c>Max == Min</c> while the buffer is shorter than
    /// the window - nothing to scroll. A value at (or within half a second of) <c>Max</c> means
    /// "follow live".
    /// </summary>
    public static (double Min, double Max, double Viewport) Scrollbar(
        double bufferStart, double latest, double spanSeconds)
    {
        double max = Math.Max(bufferStart, latest - spanSeconds);
        return (bufferStart, max, spanSeconds);
    }

    /// <summary>Whether a scrollbar value sits at the live edge (right end of its travel).</summary>
    public static bool AtLiveEdge(double value, double scrollMax) => value >= scrollMax - 0.5;
}
