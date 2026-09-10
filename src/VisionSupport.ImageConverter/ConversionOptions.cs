namespace VisionSupport.ImageConverter;

public enum ResizeKind
{
    None,
    Pixels,
    Percent,
}

public enum BitDepth
{
    Source,
    Gray8,
    Gray16,
    Bgr24,
    Bgra32,
    Rgb48,
}

public enum SaveMode
{
    /// <summary>Ask for the path - a Save dialog per file, or one folder pick for a batch.</summary>
    Ask,

    /// <summary>Straight into <see cref="ConversionOptions.OutputFolder"/> with an auto name.</summary>
    DirectToFolder,
}

/// <summary>Maps to <see cref="System.Windows.Media.Imaging.TiffCompressOption"/> without pulling
/// WPF into the settings file.</summary>
public enum TiffCompressionKind
{
    None,
    Lzw,
    Zip,
    Rle,
    Ccitt4,
}

/// <summary>
/// One set of conversion parameters - the whole right-hand options panel, and exactly what the
/// settings file persists. Plain data: the pipeline reads it, nothing here has behaviour.
/// </summary>
public sealed class ConversionOptions
{
    public ImageFormat TargetFormat { get; set; } = ImageFormat.Png;

    /// <summary>1-100. Only read when the target is JPEG.</summary>
    public int JpegQuality { get; set; } = 90;

    public TiffCompressionKind TiffCompression { get; set; } = TiffCompressionKind.Lzw;

    /// <summary>0-100. Only read when the target is JPEG XR.</summary>
    public int JpegXrQuality { get; set; } = 90;

    public ResizeKind ResizeKind { get; set; } = ResizeKind.None;

    public int ResizeWidth { get; set; } = 1920;

    public int ResizeHeight { get; set; } = 1080;

    /// <summary>When resizing to pixels, fit inside the box instead of stretching to it.</summary>
    public bool KeepAspect { get; set; } = true;

    public double ResizePercent { get; set; } = 100;

    public bool Grayscale { get; set; }

    public BitDepth BitDepth { get; set; } = BitDepth.Source;

    public SaveMode SaveMode { get; set; } = SaveMode.Ask;

    public string OutputFolder { get; set; } = "";
}
