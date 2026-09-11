using System.IO;

namespace VisionSupport.ImageConverter.Services;

/// <summary>
/// The real runner: resolves the output name with <see cref="OutputPathResolver"/> and hands the
/// file to <see cref="ConversionPipeline"/>.
/// </summary>
public sealed class ConversionRunner : IConversionRunner
{
    private readonly IImageCodec _wpfCodec;

    public ConversionRunner(IImageCodec wpfCodec) => _wpfCodec = wpfCodec;

    public string Convert(string sourcePath, string outputFolder, ConversionOptions options)
    {
        Directory.CreateDirectory(outputFolder);
        string extension = FormatCatalog.ExtensionOf(options.TargetFormat);
        string outputPath = OutputPathResolver.ResolveDirect(sourcePath, extension, outputFolder);
        ConversionPipeline.Convert(sourcePath, outputPath, options.TargetFormat, options, _wpfCodec);
        return outputPath;
    }
}
