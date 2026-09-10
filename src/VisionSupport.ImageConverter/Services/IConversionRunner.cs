namespace VisionSupport.ImageConverter.Services;

/// <summary>
/// Converts one file. Split out from the view model so the batch loop can be tested without
/// touching real pixels, and so the pixel work runs the same whether it came from a drop, a
/// button, or (later) a watched folder.
/// </summary>
public interface IConversionRunner
{
    /// <summary>Writes the converted file into <paramref name="outputFolder"/> under an
    /// auto-resolved name and returns that path. Throws on any failure.</summary>
    string Convert(string sourcePath, string outputFolder, ConversionOptions options);
}
