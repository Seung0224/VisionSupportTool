using System.Windows;
using VisionSupport.Launcher;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The menu's one part with a right answer: n items evenly spaced clockwise from twelve
/// o'clock. Y grows downward in WPF, so three o'clock is +X and six o'clock is +Y.
/// </summary>
public class RadialLayoutTests
{
    [Fact]
    public void Four_items_land_on_the_quarter_hours()
    {
        Point[] offsets = RadialLayout.Offsets(4, 100);

        Assert.Equal(4, offsets.Length);
        AssertPoint(0, -100, offsets[0]);
        AssertPoint(100, 0, offsets[1]);
        AssertPoint(0, 100, offsets[2]);
        AssertPoint(-100, 0, offsets[3]);
    }

    [Fact]
    public void Single_item_sits_at_twelve_oclock()
        => AssertPoint(0, -80, Assert.Single(RadialLayout.Offsets(1, 80)));

    [Fact]
    public void Every_offset_is_exactly_one_radius_from_the_centre()
    {
        foreach (Point p in RadialLayout.Offsets(7, 110))
        {
            Assert.Equal(110, Math.Sqrt(p.X * p.X + p.Y * p.Y), 6);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_counts_produce_nothing(int count)
        => Assert.Empty(RadialLayout.Offsets(count, 100));

    private static void AssertPoint(double x, double y, Point actual)
    {
        Assert.Equal(x, actual.X, 6);
        Assert.Equal(y, actual.Y, 6);
    }
}
