using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VisionSupport.Sam;

public static class CutoutClipboard
{
    /// <summary>
    /// Two versions of the same cutout. The "PNG" format is what Office, browsers and most editors
    /// paste with transparency intact. The plain bitmap is for everything else, and a bitmap has no
    /// alpha a pasting program will honour - left as it is, the transparent area arrives black - so
    /// that one is laid on white.
    /// </summary>
    public static DataObject CreateData(CutoutImage image)
    {
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream(CutoutFiles.EncodePng(image)));
        data.SetData(DataFormats.Bitmap, OnWhite(image));
        return data;
    }

    /// <summary>Copies with <c>copy: true</c>, so the cutout stays on the clipboard after the viewer closes.</summary>
    public static void Copy(CutoutImage image) => Clipboard.SetDataObject(CreateData(image), copy: true);

    private static BitmapSource OnWhite(CutoutImage image)
    {
        byte[] source = image.Bgra;
        var pixels = new byte[source.Length];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            int alpha = source[i + 3];
            for (int c = 0; c < 3; c++)
            {
                pixels[i + c] = (byte)((source[i + c] * alpha + 255 * (255 - alpha)) / 255);
            }
            pixels[i + 3] = 255;
        }

        BitmapSource bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null,
                                                  pixels, image.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
