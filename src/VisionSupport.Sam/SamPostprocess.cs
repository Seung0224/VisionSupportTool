namespace VisionSupport.Sam;

/// <summary>
/// A binary mask the size of a crop, row by row. <see cref="Inside"/> may be longer than
/// Width × Height: it is usually a buffer sized for the largest crop and reused.
/// </summary>
public sealed record SamMask(bool[] Inside, int Width, int Height);

/// <summary>
/// Which mask <see cref="SamPostprocess.ChooseMask"/> picked, and where that sits among the choices
/// actually available - what the level label beside the cursor reads out (<see cref="Level"/> + 1
/// of <see cref="Total"/>, so the largest mask reads "1").
/// </summary>
public readonly record struct MaskChoice(int Index, int Level, int Total);

public static class SamPostprocess
{
    /// <summary>The side of SAM 3's decoder masks.</summary>
    public const int LowRes = 288;

    public const int MaskCount = 3;

    /// <summary>Below this the decoder does not believe its own mask. The spike's throwaway mask
    /// scored 0.02 beside real ones at 0.96 and 0.98.</summary>
    public const float MinIou = 0.5f;

    /// <summary>
    /// Which of the decoder's masks to show at a granularity level, or null when its object score
    /// says nothing is under the point.
    ///
    /// Level 0 is the largest mask - usually the whole object - and each level down is the next
    /// smaller, usually a part of it. Ranked by area rather than by the decoder's own score: the
    /// score favours the tightest, most certain mask, which on a person is a shirt or a face rather
    /// than the person. Masks under <see cref="MinIou"/> are left out, unless every mask is.
    /// A level past the smallest stays on the smallest - the returned <see cref="MaskChoice.Level"/>
    /// says which one that actually was, and <see cref="MaskChoice.Total"/> how many were on offer,
    /// so a caller can show "2/3" rather than run past the end silently.
    /// </summary>
    public static MaskChoice? ChooseMask(ReadOnlySpan<float> logits, ReadOnlySpan<float> iouScores, float objectScore, int level)
    {
        if (objectScore < 0) return null;

        bool anyTrusted = false;
        for (int m = 0; m < MaskCount; m++)
        {
            if (iouScores[m] >= MinIou) anyTrusted = true;
        }

        int plane = LowRes * LowRes;
        Span<int> areas = stackalloc int[MaskCount];
        Span<int> order = stackalloc int[MaskCount];
        int count = 0;

        for (int m = 0; m < MaskCount; m++)
        {
            if (anyTrusted && iouScores[m] < MinIou) continue;

            int area = 0;
            foreach (float logit in logits.Slice(m * plane, plane))
            {
                if (logit > 0) area++;
            }

            // Insert largest first; equal areas keep the decoder's order.
            int at = count;
            while (at > 0 && areas[at - 1] < area)
            {
                areas[at] = areas[at - 1];
                order[at] = order[at - 1];
                at--;
            }
            areas[at] = area;
            order[at] = m;
            count++;
        }

        int shown = Math.Clamp(level, 0, count - 1);
        return new MaskChoice(order[shown], shown, count);
    }

    /// <summary>
    /// One of the decoder's logit maps (<c>pred_masks</c> flattened to [3,288,288]) scaled to the
    /// crop bilinearly and cut at zero, written into <paramref name="inside"/> - a buffer reused
    /// decode after decode, so every pixel of the crop is written.
    ///
    /// The logits are interpolated before thresholding, not the finished mask: a low-resolution
    /// mask stretched after the cut comes out as stairs along every edge.
    /// </summary>
    public static SamMask Upscale(ReadOnlySpan<float> logits, int maskIndex, int width, int height, bool[] inside)
    {
        Span<int> left = width <= 4096 ? stackalloc int[width] : new int[width];
        Span<int> right = width <= 4096 ? stackalloc int[width] : new int[width];
        Span<float> across = width <= 4096 ? stackalloc float[width] : new float[width];

        for (int x = 0; x < width; x++)
        {
            float fx = (x + 0.5f) * LowRes / width - 0.5f;
            int lo = Math.Clamp((int)MathF.Floor(fx), 0, LowRes - 1);
            left[x] = lo;
            right[x] = Math.Min(lo + 1, LowRes - 1);
            across[x] = Math.Clamp(fx - lo, 0f, 1f);
        }

        int offset = maskIndex * LowRes * LowRes;

        for (int y = 0; y < height; y++)
        {
            float fy = (y + 0.5f) * LowRes / height - 0.5f;
            int lo = Math.Clamp((int)MathF.Floor(fy), 0, LowRes - 1);
            int top = offset + lo * LowRes;
            int bottom = offset + Math.Min(lo + 1, LowRes - 1) * LowRes;
            float down = Math.Clamp(fy - lo, 0f, 1f);
            int row = y * width;

            for (int x = 0; x < width; x++)
            {
                float upper = logits[top + left[x]] + (logits[top + right[x]] - logits[top + left[x]]) * across[x];
                float lower = logits[bottom + left[x]] + (logits[bottom + right[x]] - logits[bottom + left[x]]) * across[x];
                inside[row + x] = upper + (lower - upper) * down > 0;
            }
        }

        return new SamMask(inside, width, height);
    }
}
