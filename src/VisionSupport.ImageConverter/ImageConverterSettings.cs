namespace VisionSupport.ImageConverter;

/// <summary>
/// Everything the converter remembers between sessions: the last option set, whether a dropped
/// folder is walked recursively, and where VisionPro lives (blank means the standard location).
/// </summary>
public sealed class ImageConverterSettings
{
    public ConversionOptions Options { get; set; } = new();

    public bool RecurseFolders { get; set; } = true;

    /// <summary>VisionPro's <c>bin</c> folder for the .idb plugin. Blank = standard install path.</summary>
    public string CognexBinPath { get; set; } = "";
}
