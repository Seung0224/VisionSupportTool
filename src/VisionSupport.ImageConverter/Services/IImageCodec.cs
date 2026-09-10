using System.Windows.Media.Imaging;

namespace VisionSupport.ImageConverter.Services;

/// <summary>Reads a file into a <see cref="BitmapSource"/> and writes one back out in a chosen
/// format. <see cref="WpfImageCodec"/> is the mainstream implementation; the Cognex .idb codec
/// comes in separately through <see cref="IImageDbCodec"/>.</summary>
public interface IImageCodec
{
    BitmapSource Decode(string path);

    void Encode(BitmapSource image, string path, ImageFormat format, ConversionOptions options);
}
