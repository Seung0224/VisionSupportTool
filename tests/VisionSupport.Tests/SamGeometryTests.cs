using VisionSupport.Sam;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Where the 1008 square is cut from and how a screen point lands in it. A wrong crop is a mask
/// drawn over the wrong pixels, and a wrong re-encode rule is either a stale mask or a GPU that
/// never rests.
/// </summary>
public class SamGeometryTests
{
    private static readonly PixelRect Primary = new(0, 0, 2560, 1600);
    private static readonly PixelRect Secondary = new(2560, 102, 1920, 1080);
    private static readonly DateTime T0 = new(2026, 9, 15, 12, 0, 0);

    [Fact]
    public void The_crop_is_centred_on_the_cursor_away_from_the_edges()
        => Assert.Equal(new PixelRect(776, 296, 1008, 1008), SamGeometry.CropAround(1280, 800, Primary));

    [Fact]
    public void Near_an_edge_the_crop_is_pushed_back_inside_the_monitor()
    {
        Assert.Equal(new PixelRect(0, 0, 1008, 1008), SamGeometry.CropAround(10, 10, Primary));
        Assert.Equal(new PixelRect(1552, 592, 1008, 1008), SamGeometry.CropAround(2550, 1590, Primary));
    }

    /// <summary>The secondary monitor starts at y=102; a cursor near its top pins the crop to that top.</summary>
    [Fact]
    public void On_a_monitor_to_the_right_the_crop_stays_on_that_monitor()
        => Assert.Equal(new PixelRect(2560, 102, 1008, 1008), SamGeometry.CropAround(2600, 200, Secondary));

    [Fact]
    public void A_monitor_smaller_than_the_crop_gives_a_crop_the_size_of_the_monitor()
        => Assert.Equal(new PixelRect(0, 0, 800, 600), SamGeometry.CropAround(400, 300, new PixelRect(0, 0, 800, 600)));

    [Fact]
    public void A_screen_point_maps_into_model_space_through_the_crop()
    {
        Assert.Equal((504f, 504f), SamGeometry.ToModel(1280, 800, new PixelRect(776, 296, 1008, 1008)));
        Assert.Equal((504f, 504f), SamGeometry.ToModel(400, 300, new PixelRect(0, 0, 800, 600)));
    }

    [Fact]
    public void With_no_encoding_yet_it_encodes()
        => Assert.True(EncodePolicy.NeedsEncode(null, 1280, 800, T0));

    [Fact]
    public void A_short_move_soon_after_reuses_the_embedding()
        => Assert.False(EncodePolicy.NeedsEncode(Centre(), 1280 + 128, 800 - 128, T0.AddMilliseconds(999)));

    [Fact]
    public void Moving_more_than_128px_on_either_axis_encodes_again()
    {
        Assert.True(EncodePolicy.NeedsEncode(Centre(), 1280 + 129, 800, T0.AddMilliseconds(10)));
        Assert.True(EncodePolicy.NeedsEncode(Centre(), 1280, 800 - 129, T0.AddMilliseconds(10)));
    }

    [Fact]
    public void After_a_second_it_encodes_again_to_follow_a_changing_screen()
        => Assert.True(EncodePolicy.NeedsEncode(Centre(), 1280, 800, T0.AddMilliseconds(1000)));

    /// <summary>A crop pushed against the monitor edge: 15px to the right is already the next monitor.</summary>
    [Fact]
    public void Leaving_the_crop_encodes_again_even_after_a_short_move()
    {
        var atEdge = new EncodeStamp(new PixelRect(1552, 592, 1008, 1008), 2550, 1000, T0);

        Assert.True(EncodePolicy.NeedsEncode(atEdge, 2565, 1000, T0.AddMilliseconds(10)));
    }

    private static EncodeStamp Centre() => new(new PixelRect(776, 296, 1008, 1008), 1280, 800, T0);
}
