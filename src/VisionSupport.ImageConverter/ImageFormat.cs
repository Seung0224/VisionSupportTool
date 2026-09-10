namespace VisionSupport.ImageConverter;

/// <summary>
/// A container the converter can write. Input files are identified by extension against
/// <see cref="Services.FormatCatalog"/> and need no enum - the WPF decoder takes whatever the OS
/// has a codec for - but output is a fixed menu, one entry per encoder the tool offers.
/// </summary>
public enum ImageFormat
{
    Bmp,
    Png,
    Jpeg,
    Gif,
    Tiff,
    JpegXr,
    Idb,
}
