namespace VisionSupport.ImageConverter.Services;

/// <summary>Which quality/compression knob, if any, an output format exposes through the WPF encoder.</summary>
public enum CompressionControl
{
    /// <summary>No adjustable setting - BMP and GIF are uncompressed/fixed, and WPF's PNG encoder
    /// is lossless deflate with no exposed level.</summary>
    None,
    JpegQuality,
    TiffCompression,
    JpegXrQuality,
}

/// <summary>
/// The one place that knows the tool's format vocabulary: what can be read, what can be written,
/// each output format's file extension, and whether that format has a compression knob to show.
/// Keeping it here stops the view and the view model from drifting apart on, say, whether PNG
/// should get a quality slider (it should not).
/// </summary>
public static class FormatCatalog
{
    /// <summary>Extensions the WPF decoder path handles. The OS may decode more (HEIC, WebP with the
    /// media extensions installed); these are the ones the tool commits to.</summary>
    private static readonly HashSet<string> InputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".png", ".jpg", ".jpeg", ".gif", ".tif", ".tiff", ".jxr", ".wdp", ".ico",
    };

    public static bool IsSupportedInput(string extension) => InputExtensions.Contains(extension);

    /// <summary>Output formats offered in the target dropdown, in menu order.</summary>
    public static IReadOnlyList<ImageFormat> OutputFormats()
    {
        var formats = new List<ImageFormat>
        {
            ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Bmp,
            ImageFormat.Tiff, ImageFormat.Gif, ImageFormat.JpegXr,
        };
        return formats;
    }

    public static CompressionControl CompressionOf(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => CompressionControl.JpegQuality,
        ImageFormat.Tiff => CompressionControl.TiffCompression,
        ImageFormat.JpegXr => CompressionControl.JpegXrQuality,
        _ => CompressionControl.None,
    };

    public static string ExtensionOf(ImageFormat format) => format switch
    {
        ImageFormat.Bmp => ".bmp",
        ImageFormat.Png => ".png",
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.Gif => ".gif",
        ImageFormat.Tiff => ".tif",
        ImageFormat.JpegXr => ".jxr",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
