using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VisionSupport.Sam;

/// <summary>A cutout as a WPF image and as a PNG file - the one format that keeps its transparency.</summary>
public static class CutoutFiles
{
    public static string DefaultName(DateTime at) => $"누끼_{at:yyyyMMdd_HHmmss}.png";

    /// <summary>Frozen, so it can be shown and encoded from any thread.</summary>
    public static BitmapSource ToBitmapSource(CutoutImage image)
    {
        BitmapSource source = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null,
                                                  image.Bgra, image.Width * 4);
        source.Freeze();
        return source;
    }

    public static byte[] EncodePng(CutoutImage image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(ToBitmapSource(image)));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static void SavePng(CutoutImage image, string path) => File.WriteAllBytes(path, EncodePng(image));
}
