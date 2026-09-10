using System.IO;

namespace VisionSupport.ImageConverter.Services;

/// <summary>
/// The real runner: resolves the output name with <see cref="OutputPathResolver"/> and hands the
/// file to <see cref="ConversionPipeline"/>. Holds the two codecs so the view model does not have
/// to know the WPF-vs-idb split.
/// </summary>
public sealed class ConversionRunner : IConversionRunner
{
    private readonly IImageCodec _wpfCodec;
    private readonly IImageDbCodec? _idbCodec;

    public ConversionRunner(IImageCodec wpfCodec, IImageDbCodec? idbCodec)
    {
        _wpfCodec = wpfCodec;
        _idbCodec = idbCodec;
    }

    public bool IdbAvailable => _idbCodec is not null;

    public string Convert(string sourcePath, string outputFolder, ConversionOptions options)
    {
        Directory.CreateDirectory(outputFolder);
        string extension = FormatCatalog.ExtensionOf(options.TargetFormat);
        string outputPath = OutputPathResolver.ResolveDirect(sourcePath, extension, outputFolder);
        ConversionPipeline.Convert(sourcePath, outputPath, options.TargetFormat, options, _wpfCodec, _idbCodec);
        return outputPath;
    }
}
