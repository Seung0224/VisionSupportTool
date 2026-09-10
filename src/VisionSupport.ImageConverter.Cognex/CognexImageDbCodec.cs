using System.IO;
using System.Windows.Media.Imaging;
using VisionSupport.ImageConverter.Services;

namespace VisionSupport.ImageConverter.Cognex;

/// <summary>
/// Reads and writes Cognex <c>.idb</c> image databases via the VisionPro <c>CogImageFile</c> API.
///
/// This type is loaded by reflection through <see cref="CognexCodecLoader"/>, so it must have a
/// public parameterless constructor and must not throw from it. When the project was built
/// without VisionPro (no <c>COGNEX</c> define) every method is an inert stub and
/// <see cref="CanDecode"/>/<see cref="CanEncode"/> return false, which keeps <c>.idb</c> out of
/// the converter's menus.
/// </summary>
public sealed class CognexImageDbCodec : IImageDbCodec
{
    private static bool IsIdb(string extension)
        => extension.Equals(".idb", StringComparison.OrdinalIgnoreCase);

#if COGNEX
    public bool CanDecode(string extension) => IsIdb(extension);

    public bool CanEncode(string extension) => IsIdb(extension);

    public BitmapSource Decode(string path)
    {
        using var file = new global::Cognex.VisionPro.ImageFile.CogImageFile();
        file.Open(path, global::Cognex.VisionPro.ImageFile.CogImageFileModeConstants.Read);
        if (file.Count == 0) throw new InvalidDataException($"'{path}' contains no image frames.");

        using System.Drawing.Bitmap bitmap = file[0].ToBitmap();
        return ToBitmapSource(bitmap);
    }

    public void Encode(BitmapSource image, string path)
    {
        using System.Drawing.Bitmap bitmap = ToGdiBitmap(image);
        global::Cognex.VisionPro.ICogImage cogImage = bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format8bppIndexed
            ? new global::Cognex.VisionPro.CogImage8Grey(bitmap)
            : new global::Cognex.VisionPro.CogImage24PlanarColor(bitmap);

        if (File.Exists(path)) File.Delete(path);
        using var file = new global::Cognex.VisionPro.ImageFile.CogImageFile();
        file.Open(path, global::Cognex.VisionPro.ImageFile.CogImageFileModeConstants.Write);
        file.Append(cogImage);
        file.Close();
    }

    private static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        BitmapFrame frame = BitmapDecoder
            .Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }

    private static System.Drawing.Bitmap ToGdiBitmap(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return new System.Drawing.Bitmap(stream);
    }
#else
    public bool CanDecode(string extension) => false;

    public bool CanEncode(string extension) => false;

    public BitmapSource Decode(string path)
        => throw new NotSupportedException("Cognex .idb support needs VisionPro installed on this machine.");

    public void Encode(BitmapSource image, string path)
        => throw new NotSupportedException("Cognex .idb support needs VisionPro installed on this machine.");
#endif
}
