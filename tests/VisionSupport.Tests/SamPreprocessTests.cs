using VisionSupport.Sam;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// What the encoder is fed. SAM 3 wants RGB planes scaled to -1..1; swapped channels or the wrong
/// normalisation still produce a mask, just a worse one, so nothing else would catch it. The tensor
/// is a buffer reused crop after crop, so every value must be written, never left over.
/// </summary>
public class SamPreprocessTests
{
    private const int Side = 1008;
    private const int Plane = Side * Side;

    [Fact]
    public void Pixels_become_rgb_planes_scaled_to_minus_one_to_one()
    {
        var bgra = new byte[Side * Side * 4];
        bgra[2] = 255;        // (0,0) pure red
        bgra[4 + 1] = 255;    // (1,0) pure green
        var tensor = new float[3 * Plane];
        tensor[5] = 99f;      // left over from a previous crop

        SamPreprocess.ToTensor(bgra, Side, Side, tensor);

        Assert.Equal(1f, tensor[0], 4);
        Assert.Equal(-1f, tensor[Plane], 4);
        Assert.Equal(-1f, tensor[2 * Plane], 4);
        Assert.Equal(1f, tensor[Plane + 1], 4);
        Assert.Equal(-1f, tensor[5], 4);
    }

    [Fact]
    public void A_crop_smaller_than_1008_is_stretched_to_fill_the_model_input()
    {
        var bgra = new byte[2 * 2 * 4];
        bgra[(1 * 2 + 1) * 4 + 2] = 255;    // bottom-right pixel red
        var tensor = new float[3 * Plane];

        SamPreprocess.ToTensor(bgra, 2, 2, tensor);

        Assert.Equal(1f, tensor[1007 * Side + 1007], 4);
        Assert.Equal(-1f, tensor[503 * Side + 503], 4);
    }
}
