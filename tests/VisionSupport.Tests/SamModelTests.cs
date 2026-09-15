using VisionSupport.Sam;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The real model on the real GPU. It runs only where the model has been downloaded - elsewhere it
/// returns without asserting, because a test suite that needs a gigabyte and a GPU would not be run.
///
/// It also runs under dotnet's test host, which is exactly the process layout where Windows' own
/// DirectML 1.0 used to be picked up instead of the redistributable, so it guards that fix too.
/// </summary>
public class SamModelTests
{
    [Fact]
    public void With_the_model_present_a_point_inside_a_square_selects_the_square()
    {
        var store = new ModelStore();
        if (!store.IsComplete()) return;

        const int side = SamGeometry.ModelSide;
        var bgra = new byte[side * side * 4];
        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
        {
            int i = (y * side + x) * 4;
            byte value = x is >= 300 and < 500 && y is >= 300 and < 500 ? (byte)255 : (byte)128;
            bgra[i] = bgra[i + 1] = bgra[i + 2] = value;
        }

        var tensor = new float[3 * side * side];
        SamPreprocess.ToTensor(bgra, side, side, tensor);

        using SamModel model = SamModel.Load(store.EncoderPath, store.DecoderPath);
        using SamEmbedding embedding = model.CreateEmbedding();
        model.Encode(tensor, embedding);
        SamDecodeResult result = model.Decode(embedding, 400, 400);

        MaskChoice? choice = SamPostprocess.ChooseMask(result.MaskLogits, result.IouScores, result.ObjectScore, level: 0);
        Assert.NotNull(choice);

        SamMask mask = SamPostprocess.Upscale(result.MaskLogits, choice!.Value.Index, side, side, new bool[side * side]);
        Assert.True(mask.Inside[400 * side + 400]);
        Assert.True(mask.Inside[310 * side + 310]);
        Assert.False(mask.Inside[600 * side + 600]);
        Assert.False(mask.Inside[100 * side + 100]);
    }
}
