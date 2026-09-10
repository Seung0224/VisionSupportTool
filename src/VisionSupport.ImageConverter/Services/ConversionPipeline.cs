using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VisionSupport.ImageConverter.Services;

/// <summary>
/// One file in, one file out: decode -> resize -> grayscale -> bit depth -> encode. The .idb
/// codec, when present, handles its own extension on either end; everything else goes through the
/// WPF codec.
/// </summary>
public static class ConversionPipeline
{
    public static void Convert(string sourcePath, string outputPath, ImageFormat target,
        ConversionOptions options, IImageCodec wpfCodec, IImageDbCodec? idbCodec)
    {
        string sourceExtension = Path.GetExtension(sourcePath);

        BitmapSource image = idbCodec is not null && idbCodec.CanDecode(sourceExtension)
            ? idbCodec.Decode(sourcePath)
            : wpfCodec.Decode(sourcePath);

        image = Resize(image, options);
        if (options.Grayscale) image = new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0);
        image = ConvertBitDepth(image, options.BitDepth);

        if (image.CanFreeze && !image.IsFrozen) image.Freeze();

        if (target == ImageFormat.Idb)
        {
            if (idbCodec is null) throw new NotSupportedException("The Cognex .idb codec is not loaded.");
            idbCodec.Encode(image, outputPath);
        }
        else
        {
            wpfCodec.Encode(image, outputPath, target, options);
        }
    }

    private static BitmapSource Resize(BitmapSource image, ConversionOptions options)
    {
        double scaleX, scaleY;

        switch (options.ResizeKind)
        {
            case ResizeKind.Percent:
                scaleX = scaleY = options.ResizePercent / 100.0;
                break;

            case ResizeKind.Pixels:
                if (options.ResizeWidth <= 0 || options.ResizeHeight <= 0) return image;
                scaleX = (double)options.ResizeWidth / image.PixelWidth;
                scaleY = (double)options.ResizeHeight / image.PixelHeight;
                if (options.KeepAspect) scaleX = scaleY = Math.Min(scaleX, scaleY);
                break;

            default:
                return image;
        }

        if (scaleX <= 0 || scaleY <= 0) return image;
        if (Math.Abs(scaleX - 1.0) < 1e-9 && Math.Abs(scaleY - 1.0) < 1e-9) return image;

        return new TransformedBitmap(image, new ScaleTransform(scaleX, scaleY));
    }

    private static BitmapSource ConvertBitDepth(BitmapSource image, BitDepth depth)
    {
        PixelFormat? target = depth switch
        {
            BitDepth.Gray8 => PixelFormats.Gray8,
            BitDepth.Gray16 => PixelFormats.Gray16,
            BitDepth.Bgr24 => PixelFormats.Bgr24,
            BitDepth.Bgra32 => PixelFormats.Bgra32,
            BitDepth.Rgb48 => PixelFormats.Rgb48,
            _ => null,
        };

        return target is null || image.Format == target
            ? image
            : new FormatConvertedBitmap(image, target.Value, null, 0);
    }
}
