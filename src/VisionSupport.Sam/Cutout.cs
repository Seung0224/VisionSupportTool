namespace VisionSupport.Sam;

/// <summary>A cut-out object: 32bpp BGRA, straight alpha, transparent around the object.</summary>
public sealed record CutoutImage(byte[] Bgra, int Width, int Height);

public static class Cutout
{
    /// <summary>
    /// The object under the mask, cropped to the mask's bounding box, or null when the mask is empty.
    ///
    /// Inside the mask colour is copied and alpha set opaque - GDI captures arrive with alpha 0.
    /// Outside it every channel is zero rather than hidden colour with alpha 0, so a program that
    /// ignores alpha still sees a clean background instead of the screen that was behind the object.
    /// </summary>
    public static CutoutImage? Make(ReadOnlySpan<byte> bgra, SamMask mask)
    {
        int left = mask.Width, top = mask.Height, right = -1, bottom = -1;
        for (int y = 0; y < mask.Height; y++)
        {
            for (int x = 0; x < mask.Width; x++)
            {
                if (!mask.Inside[y * mask.Width + x]) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < 0) return null;

        int width = right - left + 1;
        int height = bottom - top + 1;
        var pixels = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int source = (top + y) * mask.Width + left + x;
                if (!mask.Inside[source]) continue;

                int s = source * 4;
                int d = (y * width + x) * 4;
                pixels[d] = bgra[s];
                pixels[d + 1] = bgra[s + 1];
                pixels[d + 2] = bgra[s + 2];
                pixels[d + 3] = 255;
            }
        }

        return new CutoutImage(pixels, width, height);
    }
}
