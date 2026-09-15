using VisionSupport.Sam;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// What is left of a mask before it is shown or cut: the one piece the user is pointing at, with
/// pinholes filled but real gaps - between an arm and a body - left open. The work buffers are reused
/// decode after decode, so one test runs two masks through the same buffers.
/// </summary>
public class MaskCleanupTests
{
    private const int W = 100, H = 100;

    [Fact]
    public void Only_the_piece_under_the_cursor_is_kept()
    {
        var inside = new bool[W * H];
        Fill(inside, 10, 10, 30, 30, true);
        Fill(inside, 60, 60, 90, 90, true);

        Assert.True(MaskCleanup.Clean(new SamMask(inside, W, H), 20, 20, new byte[W * H], new int[W * H]));

        Assert.True(inside[20 * W + 20]);
        Assert.False(inside[70 * W + 70]);
    }

    [Fact]
    public void With_the_cursor_off_the_mask_the_largest_piece_is_kept()
    {
        var inside = new bool[W * H];
        Fill(inside, 10, 10, 20, 20, true);
        Fill(inside, 50, 50, 90, 90, true);

        MaskCleanup.Clean(new SamMask(inside, W, H), 0, 0, new byte[W * H], new int[W * H]);

        Assert.False(inside[15 * W + 15]);
        Assert.True(inside[70 * W + 70]);
    }

    /// <summary>An 80×80 object with a 2×2 pinhole and a 20×20 gap. 1% of the object is about 60px.</summary>
    [Fact]
    public void Pinholes_are_filled_and_real_gaps_are_left_open()
    {
        var inside = new bool[W * H];
        Fill(inside, 10, 10, 90, 90, true);
        Fill(inside, 30, 30, 32, 32, false);
        Fill(inside, 50, 50, 70, 70, false);

        MaskCleanup.Clean(new SamMask(inside, W, H), 20, 20, new byte[W * H], new int[W * H]);

        Assert.True(inside[30 * W + 30]);
        Assert.False(inside[60 * W + 60]);
        Assert.False(inside[0]);
    }

    [Fact]
    public void An_empty_mask_has_nothing_to_keep()
        => Assert.False(MaskCleanup.Clean(new SamMask(new bool[W * H], W, H), 5, 5, new byte[W * H], new int[W * H]));

    [Fact]
    public void Marks_left_by_the_previous_mask_do_not_affect_the_next()
    {
        var marks = new byte[W * H];
        var queue = new int[W * H];

        var first = new bool[W * H];
        Fill(first, 10, 10, 30, 30, true);
        MaskCleanup.Clean(new SamMask(first, W, H), 20, 20, marks, queue);

        var second = new bool[W * H];
        Fill(second, 60, 60, 90, 90, true);
        Assert.True(MaskCleanup.Clean(new SamMask(second, W, H), 70, 70, marks, queue));

        Assert.True(second[70 * W + 70]);
    }

    private static void Fill(bool[] inside, int x0, int y0, int x1, int y1, bool value)
    {
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
            inside[y * W + x] = value;
    }
}
