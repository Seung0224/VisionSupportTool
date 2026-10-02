using VisionSupport.Wireshark.ViewModels;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>The comm monitor's chart pans and zooms the way the memory monitor's does.</summary>
public class WiresharkChartTests
{
    [Fact]
    public void Following_live_pins_the_newest_sample_to_the_right_edge()
        => Assert.Equal((8200d, 10_000d), ChartTimeWindow.View(0, 10_000, 1800, followLive: true, desiredStart: 0));

    [Fact]
    public void A_frozen_window_stays_inside_the_retained_history()
    {
        Assert.Equal((0d, 600d), ChartTimeWindow.View(0, 10_000, 600, followLive: false, desiredStart: -50));
        Assert.Equal((9400d, 10_000d), ChartTimeWindow.View(0, 10_000, 600, followLive: false, desiredStart: 99_999));
    }

    [Fact]
    public void Dragging_the_scrollbar_to_its_right_end_means_live_again()
    {
        (double min, double max, double viewport) = ChartTimeWindow.Scrollbar(0, 10_000, 600);

        Assert.Equal((0d, 9400d, 600d), (min, max, viewport));
        Assert.True(ChartTimeWindow.AtLiveEdge(9400, max));
        Assert.False(ChartTimeWindow.AtLiveEdge(9000, max));
    }

    /// <summary>X is seconds since the first sample ever taken, so trimming old samples does not
    /// slide a panned window.</summary>
    [Fact]
    public void Chart_seconds_stay_put_when_old_samples_are_trimmed()
    {
        var h = new ChartHistory(capacity: 2);
        h.Add(TestFrames.T0, 1, 1);
        h.Add(TestFrames.T0.AddSeconds(1), 2, 2);
        h.Add(TestFrames.T0.AddSeconds(2), 3, 3);

        Assert.Equal(TestFrames.T0, h.Start);
        Assert.Equal(new double[] { 1, 2 }, h.Seconds());
    }
}
