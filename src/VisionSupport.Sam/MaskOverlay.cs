namespace VisionSupport.Sam;

/// <summary>
/// What is laid over the crop while hovering: dark outside the mask, clear inside it so the live
/// screen shows at full brightness, and a white band on the edge. The band is for the dark object
/// on a dark background, which brightening alone does not separate.
///
/// Both methods write into buffers the caller reuses for the whole mode - at 4MB a picture, fresh
/// arrays per decode were most of the memory that piled up between uses - so every pixel is written.
/// </summary>
public static class MaskOverlay
{
    public const int EdgeWidth = 2;

    /// <summary>
    /// 32bpp BGRA, straight alpha, mask-sized, into <paramref name="pixels"/>. <paramref name="scratch"/>
    /// holds one row pass of the edge test and needs Width × Height elements.
    ///
    /// Pixels beyond the crop count as outside, so a mask the crop cuts off gets an edge along the
    /// cut - which is where the cutout will actually end.
    ///
    /// The edge test is a box dilation of "outside" split into a horizontal and a vertical pass, so
    /// each pixel looks along one line at a time instead of around a whole square.
    /// </summary>
    public static void Render(SamMask mask, byte dimAlpha, byte[] pixels, bool[] scratch)
    {
        int width = mask.Width;
        int height = mask.Height;
        bool[] inside = mask.Inside;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                bool near = false;
                for (int d = -EdgeWidth; d <= EdgeWidth && !near; d++)
                {
                    int nx = x + d;
                    near = nx < 0 || nx >= width || !inside[row + nx];
                }
                scratch[row + x] = near;
            }
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                int p = i * 4;

                if (!inside[i])
                {
                    pixels[p] = pixels[p + 1] = pixels[p + 2] = 0;
                    pixels[p + 3] = dimAlpha;
                    continue;
                }

                bool edge = false;
                for (int d = -EdgeWidth; d <= EdgeWidth && !edge; d++)
                {
                    int ny = y + d;
                    edge = ny < 0 || ny >= height || scratch[ny * width + x];
                }

                byte value = edge ? (byte)255 : (byte)0;
                pixels[p] = pixels[p + 1] = pixels[p + 2] = pixels[p + 3] = value;
            }
        }
    }

    /// <summary>The overlay when nothing is under the cursor: dark all over.</summary>
    public static void Dim(int width, int height, byte dimAlpha, byte[] pixels)
    {
        int length = width * height * 4;
        for (int p = 0; p < length; p += 4)
        {
            pixels[p] = pixels[p + 1] = pixels[p + 2] = 0;
            pixels[p + 3] = dimAlpha;
        }
    }
}
