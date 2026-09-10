using System.IO;
using System.Windows.Media.Imaging;

namespace VisionSupport.ImageConverter.Services;

/// <summary>
/// The mainstream codec: whatever the OS has a WPF decoder for on the way in, and one of BMP /
/// PNG / JPEG / GIF / TIFF / JPEG XR on the way out. PNG has no adjustable compression here -
/// WPF's encoder is fixed lossless deflate - so the options panel hides the slider for it.
/// </summary>
public sealed class WpfImageCodec : IImageCodec
{
    public BitmapSource Decode(string path)
    {
        var decoder = BitmapDecoder.Create(
            new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapFrame frame = decoder.Frames[0];
        if (frame.CanFreeze) frame.Freeze();
        return frame;
    }

    public void Encode(BitmapSource image, string path, ImageFormat format, ConversionOptions options)
    {
        BitmapEncoder encoder = format switch
        {
            ImageFormat.Bmp => new BmpBitmapEncoder(),
            ImageFormat.Png => new PngBitmapEncoder(),
            ImageFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = Clamp(options.JpegQuality, 1, 100) },
            ImageFormat.Gif => new GifBitmapEncoder(),
            ImageFormat.Tiff => new TiffBitmapEncoder { Compression = ToTiffOption(options.TiffCompression) },
            ImageFormat.JpegXr => new WmpBitmapEncoder { ImageQualityLevel = Clamp(options.JpegXrQuality, 0, 100) / 100f },
            _ => throw new NotSupportedException($"{format} is not a WPF-encodable format."),
        };

        encoder.Frames.Add(BitmapFrame.Create(image));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    private static TiffCompressOption ToTiffOption(TiffCompressionKind kind) => kind switch
    {
        TiffCompressionKind.None => TiffCompressOption.None,
        TiffCompressionKind.Lzw => TiffCompressOption.Lzw,
        TiffCompressionKind.Zip => TiffCompressOption.Zip,
        TiffCompressionKind.Rle => TiffCompressOption.Rle,
        TiffCompressionKind.Ccitt4 => TiffCompressOption.Ccitt4,
        _ => TiffCompressOption.Default,
    };
}
