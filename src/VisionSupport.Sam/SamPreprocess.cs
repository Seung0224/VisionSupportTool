namespace VisionSupport.Sam;

public static class SamPreprocess
{
    // From SAM 3's preprocessor_config.json: every channel scaled to 0-1, then to -1..1.
    private const float Mean = 0.5f;
    private const float Std = 0.5f;

    /// <summary>
    /// A BGRA crop as the encoder's <c>pixel_values</c>: R, G and B planes at
    /// <see cref="SamGeometry.ModelSide"/> square, written into <paramref name="tensor"/>.
    ///
    /// The tensor is a buffer the caller keeps for the whole mode - at 12MB a fresh one per crop is
    /// the kind of allocation that was piling up between uses - so every element is written.
    ///
    /// A crop smaller than the model side (a small monitor) is stretched nearest-neighbour. At the
    /// usual full-size crop the mapping is the identity.
    /// </summary>
    public static void ToTensor(ReadOnlySpan<byte> bgra, int width, int height, float[] tensor)
    {
        int side = SamGeometry.ModelSide;
        int plane = side * side;

        for (int y = 0; y < side; y++)
        {
            int sourceRow = y * height / side * width;
            int row = y * side;

            for (int x = 0; x < side; x++)
            {
                int i = (sourceRow + x * width / side) * 4;
                int o = row + x;
                tensor[o] = (bgra[i + 2] / 255f - Mean) / Std;
                tensor[plane + o] = (bgra[i + 1] / 255f - Mean) / Std;
                tensor[2 * plane + o] = (bgra[i] / 255f - Mean) / Std;
            }
        }
    }
}
