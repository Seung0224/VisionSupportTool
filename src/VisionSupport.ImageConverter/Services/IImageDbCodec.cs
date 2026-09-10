using System.Windows.Media.Imaging;

namespace VisionSupport.ImageConverter.Services;

/// <summary>
/// The seam for Cognex .idb support. The contract lives in this assembly; the implementation is
/// a separate optional project (VisionSupport.ImageConverter.Cognex) that references the VisionPro
/// assemblies and is loaded at runtime by <see cref="CognexCodecLoader"/>. When VisionPro is not
/// installed the loader returns null and the converter simply drops .idb from its menus.
/// </summary>
public interface IImageDbCodec
{
    bool CanDecode(string extension);

    bool CanEncode(string extension);

    BitmapSource Decode(string path);

    void Encode(BitmapSource image, string path);
}
