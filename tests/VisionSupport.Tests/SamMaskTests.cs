using VisionSupport.Sam;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// From the decoder's three low-resolution guesses to what the user sees and keeps: which guess is
/// shown, how it is scaled up, how it is drawn over the dimmed screen, and what the cutout contains.
///
/// The mask and overlay buffers are reused decode after decode, so the tests start them dirty: a
/// pixel left over from the previous object would show as a stray speck of highlight.
/// </summary>
public class SamMaskTests
{
    private const int Side = 1008;

    private static readonly float[] Trusted = { 0.9f, 0.9f, 0.9f };

    /// <summary>
    /// Mask 0 covers 100 low-res pixels, mask 1 covers 10, mask 2 covers 1000. The level that comes
    /// back is the one actually shown - past the smallest it stays on the smallest - because it is
    /// what the label beside the cursor reads out.
    /// </summary>
    [Fact]
    public void The_largest_mask_is_shown_first_and_each_level_down_is_the_next_smaller()
    {
        float[] logits = LogitsWithAreas(100, 10, 1000);

        Assert.Equal(new MaskChoice(2, 0, 3), SamPostprocess.ChooseMask(logits, Trusted, objectScore: 3f, level: 0));
        Assert.Equal(new MaskChoice(0, 1, 3), SamPostprocess.ChooseMask(logits, Trusted, objectScore: 3f, level: 1));
        Assert.Equal(new MaskChoice(1, 2, 3), SamPostprocess.ChooseMask(logits, Trusted, objectScore: 3f, level: 2));
        Assert.Equal(new MaskChoice(1, 2, 3), SamPostprocess.ChooseMask(logits, Trusted, objectScore: 3f, level: 7));
    }

    /// <summary>
    /// SAM 3 returns a throwaway mask beside the real ones - the spike's scored 0.02 against 0.96 and
    /// 0.98. Ranked by size alone, a large throwaway would become "the whole object". Leaving it out
    /// also leaves one level fewer.
    /// </summary>
    [Fact]
    public void A_mask_the_decoder_does_not_believe_is_left_out()
    {
        float[] logits = LogitsWithAreas(100, 5000, 1000);
        float[] iou = { 0.9f, 0.02f, 0.9f };

        Assert.Equal(new MaskChoice(2, 0, 2), SamPostprocess.ChooseMask(logits, iou, objectScore: 3f, level: 0));
        Assert.Equal(new MaskChoice(0, 1, 2), SamPostprocess.ChooseMask(logits, iou, objectScore: 3f, level: 1));
        Assert.Equal(new MaskChoice(0, 1, 2), SamPostprocess.ChooseMask(logits, iou, objectScore: 3f, level: 2));
    }

    [Fact]
    public void A_negative_object_score_means_nothing_is_there()
        => Assert.Null(SamPostprocess.ChooseMask(LogitsWithAreas(100, 10, 1000), Trusted, objectScore: -0.1f, level: 0));

    private static float[] LogitsWithAreas(params int[] areas)
    {
        const int plane = 288 * 288;
        var logits = new float[3 * plane];
        Array.Fill(logits, -1f);
        for (int m = 0; m < 3; m++)
        {
            for (int i = 0; i < areas[m]; i++) logits[m * plane + i] = 1f;
        }
        return logits;
    }

    [Fact]
    public void Upscaling_keeps_the_positive_region_of_the_chosen_mask()
    {
        // Mask 1: left half positive. Masks 0 and 2 are the opposite, so reading the wrong one fails.
        const int low = 288;
        var logits = new float[3 * low * low];
        for (int m = 0; m < 3; m++)
        for (int y = 0; y < low; y++)
        for (int x = 0; x < low; x++)
            logits[m * low * low + y * low + x] = (x < low / 2) == (m == 1) ? 5f : -5f;

        var buffer = new bool[Side * Side];
        buffer[Side * Side - 1] = true;

        SamMask mask = SamPostprocess.Upscale(logits, 1, Side, Side, buffer);

        Assert.Same(buffer, mask.Inside);
        Assert.Equal((Side, Side), (mask.Width, mask.Height));
        Assert.True(mask.Inside[0]);
        Assert.True(mask.Inside[Side * 500 + 500]);
        Assert.False(mask.Inside[Side * 500 + 524]);
        Assert.False(mask.Inside[Side * Side - 1]);
    }

    /// <summary>9×9 with a 7×7 block inside: the two outer rings of the block are the edge.</summary>
    [Fact]
    public void The_overlay_is_clear_inside_dark_outside_and_white_on_a_2px_edge()
    {
        var inside = new bool[81];
        for (int y = 1; y <= 7; y++)
        for (int x = 1; x <= 7; x++)
            inside[y * 9 + x] = true;

        var pixels = new byte[81 * 4];
        Array.Fill(pixels, (byte)7);

        MaskOverlay.Render(new SamMask(inside, 9, 9), dimAlpha: 140, pixels, new bool[81]);

        byte[] At(int x, int y) => pixels[((y * 9 + x) * 4)..((y * 9 + x) * 4 + 4)];
        Assert.Equal(new byte[] { 0, 0, 0, 140 }, At(0, 0));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, At(1, 4));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, At(2, 4));
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, At(3, 4));
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, At(4, 4));
    }

    [Fact]
    public void With_nothing_under_the_cursor_the_whole_overlay_is_dark()
    {
        var pixels = new byte[24];
        Array.Fill(pixels, (byte)9);

        MaskOverlay.Dim(3, 2, dimAlpha: 140, pixels);

        for (int i = 0; i < 24; i += 4) Assert.Equal(new byte[] { 0, 0, 0, 140 }, pixels[i..(i + 4)]);
    }

    /// <summary>
    /// 4×4 crop whose blue channel is the pixel index, with alpha 0 as GDI captures deliver it.
    /// An L-shaped mask over (1,1), (2,1), (1,2).
    /// </summary>
    [Fact]
    public void The_cutout_is_cropped_to_the_mask_and_transparent_outside_it()
    {
        var bgra = new byte[4 * 4 * 4];
        for (int i = 0; i < 16; i++)
        {
            bgra[i * 4] = (byte)i;
            bgra[i * 4 + 1] = 100;
            bgra[i * 4 + 2] = 200;
        }
        var inside = new bool[16];
        inside[1 * 4 + 1] = inside[1 * 4 + 2] = inside[2 * 4 + 1] = true;

        CutoutImage? image = Cutout.Make(bgra, new SamMask(inside, 4, 4));

        Assert.NotNull(image);
        Assert.Equal((2, 2), (image!.Width, image.Height));
        Assert.Equal(new byte[] { 5, 100, 200, 255 }, image.Bgra[0..4]);
        Assert.Equal(new byte[] { 6, 100, 200, 255 }, image.Bgra[4..8]);
        Assert.Equal(new byte[] { 9, 100, 200, 255 }, image.Bgra[8..12]);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, image.Bgra[12..16]);
    }

    [Fact]
    public void An_empty_mask_gives_no_cutout()
        => Assert.Null(Cutout.Make(new byte[16], new SamMask(new bool[4], 2, 2)));
}
