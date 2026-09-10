using MemMon;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The monitor chart shows a window of the chosen width that either follows the newest sample
/// (newest pinned to the right edge) or sits where the scrollbar left it. These pin the edge
/// cases of that window and of the scrollbar geometry that pans it.
/// </summary>
public class ChartTimeWindowTests
{
    [Fact]
    public void View_following_live_pins_the_newest_sample_to_the_right_edge()
    {
        var (start, end) = ChartTimeWindow.View(
            bufferStart: 0, latest: 10_000, spanSeconds: 1800, followLive: true, desiredStart: 0);

        Assert.Equal(8200, start);
        Assert.Equal(10_000, end);
    }

    [Fact]
    public void View_live_session_shorter_than_the_window_fills_from_the_right_without_a_gap()
    {
        var (start, end) = ChartTimeWindow.View(
            bufferStart: 0, latest: 300, spanSeconds: 1800, followLive: true, desiredStart: 0);

        Assert.Equal(0, start);      // no negative left edge...
        Assert.Equal(300, end);      // ...and the trace still ends at "now" on the right
    }

    [Fact]
    public void View_live_keeps_a_minimum_axis_width_in_the_first_seconds()
    {
        var (start, end) = ChartTimeWindow.View(
            bufferStart: 0, latest: 20, spanSeconds: 1800, followLive: true, desiredStart: 0);

        Assert.Equal(-40, start);
        Assert.Equal(20, end);
        Assert.True(end - start >= 60);
    }

    [Fact]
    public void View_frozen_sits_at_the_scrollbar_position()
    {
        var (start, end) = ChartTimeWindow.View(
            bufferStart: 0, latest: 10_000, spanSeconds: 1800, followLive: false, desiredStart: 3000);

        Assert.Equal(3000, start);
        Assert.Equal(4800, end);
    }

    [Fact]
    public void View_frozen_cannot_scroll_past_the_live_edge()
    {
        var (start, end) = ChartTimeWindow.View(
            bufferStart: 0, latest: 10_000, spanSeconds: 1800, followLive: false, desiredStart: 9_999);

        Assert.Equal(8200, start);
        Assert.Equal(10_000, end);
    }

    [Fact]
    public void View_frozen_cannot_scroll_before_the_retained_buffer()
    {
        var (start, _) = ChartTimeWindow.View(
            bufferStart: 5000, latest: 40_000, spanSeconds: 1800, followLive: false, desiredStart: 1000);

        Assert.Equal(5000, start);
    }

    [Fact]
    public void View_frozen_does_not_throw_when_the_buffer_is_shorter_than_the_window()
    {
        var (start, end) = ChartTimeWindow.View(
            bufferStart: 0, latest: 600, spanSeconds: 1800, followLive: false, desiredStart: 100);

        Assert.Equal(0, start);
        Assert.Equal(1800, end);
    }

    [Fact]
    public void Scrollbar_range_covers_every_window_start_from_oldest_to_live()
    {
        var (min, max, viewport) = ChartTimeWindow.Scrollbar(bufferStart: 0, latest: 43_200, spanSeconds: 1800);

        Assert.Equal(0, min);
        Assert.Equal(41_400, max);   // left edge at Max => window ends exactly at "now"
        Assert.Equal(1800, viewport);
    }

    [Fact]
    public void Scrollbar_travel_widens_as_the_window_narrows()
    {
        var (_, maxWide, _) = ChartTimeWindow.Scrollbar(0, 43_200, spanSeconds: 1800);
        var (_, maxNarrow, _) = ChartTimeWindow.Scrollbar(0, 43_200, spanSeconds: 300);

        Assert.True(maxNarrow > maxWide);
    }

    [Fact]
    public void Scrollbar_has_no_travel_until_the_buffer_outgrows_the_window()
    {
        var (min, max, _) = ChartTimeWindow.Scrollbar(bufferStart: 0, latest: 800, spanSeconds: 1800);

        Assert.Equal(min, max);
    }

    [Fact]
    public void AtLiveEdge_is_true_at_and_just_below_the_scroll_maximum()
    {
        Assert.True(ChartTimeWindow.AtLiveEdge(value: 42_000, scrollMax: 42_000));
        Assert.True(ChartTimeWindow.AtLiveEdge(value: 41_999.7, scrollMax: 42_000));
        Assert.False(ChartTimeWindow.AtLiveEdge(value: 41_000, scrollMax: 42_000));
    }
}
