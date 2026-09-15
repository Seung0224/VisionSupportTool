using System.Windows;
using VisionSupport.Sam;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The screen side of the cutout mode: which monitor a point is on, where a crop lands inside that
/// monitor's overlay window, and the capture buffer the crops are cut from.
/// </summary>
public class SamScreenTests
{
    private static readonly MonitorInfo[] Desk =
    {
        new(new PixelRect(0, 0, 2560, 1600), 1.0),
        new(new PixelRect(2560, 102, 1920, 1080), 1.0),
    };

    [Fact]
    public void A_point_belongs_to_the_monitor_that_contains_it()
    {
        Assert.Same(Desk[0], Monitors.At(Desk, 2559, 1599));
        Assert.Same(Desk[1], Monitors.At(Desk, 2560, 102));
    }

    /// <summary>Above the secondary monitor's top edge there is no screen at all.</summary>
    [Fact]
    public void A_point_in_the_gap_between_monitors_belongs_to_none()
        => Assert.Null(Monitors.At(Desk, 3000, 50));

    [Fact]
    public void A_crop_is_placed_in_the_overlay_relative_to_its_monitor_and_scaled_to_DIPs()
    {
        Rect dips = SamGeometry.ToDips(new PixelRect(3584, 614, 1024, 1024), new PixelRect(2560, 102, 1920, 1080), 1.5);

        Assert.Equal(1024 / 1.5, dips.X, 6);
        Assert.Equal(512 / 1.5, dips.Y, 6);
        Assert.Equal(1024 / 1.5, dips.Width, 6);
        Assert.Equal(1024 / 1.5, dips.Height, 6);
    }

    /// <summary>
    /// Captures the real screen's top-left corner, so it checks sizes and offsets rather than content:
    /// a region's pixel must be the same bytes as that position in the whole frame.
    /// </summary>
    [Fact]
    public void A_region_copy_holds_that_part_of_the_frame_and_refuses_to_reach_outside_it()
    {
        using var frame = new ScreenFrame(new PixelRect(0, 0, 64, 48));

        Assert.Equal(0, frame.Version);
        frame.Capture();
        Assert.Equal(1, frame.Version);

        var region = new byte[16 * 8 * 4];
        frame.CopyRegion(new PixelRect(10, 10, 16, 8), region);

        byte[]? whole = null;
        frame.WithLatest(pixels => whole = pixels.ToArray());
        int inRegion = (2 * 16 + 3) * 4;
        int inWhole = ((10 + 2) * 64 + 10 + 3) * 4;
        Assert.Equal(whole![inWhole..(inWhole + 4)], region[inRegion..(inRegion + 4)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => frame.CopyRegion(new PixelRect(60, 0, 16, 8), region));
        Assert.Throws<ArgumentException>(() => frame.CopyRegion(new PixelRect(10, 10, 16, 8), new byte[10]));
    }
}
