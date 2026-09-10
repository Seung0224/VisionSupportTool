using System.IO;

namespace VisionSupport.ImageConverter.Services;

/// <summary>
/// Decides the output path when the user picked "save straight to a folder" instead of a dialog.
/// The rule the user asked for: keep the original file name, swap the extension, and never
/// overwrite - a taken name gets " (1)", " (2)" and so on until one is free. Because the check
/// is "does this file exist", converting a file in place to the same format also lands on a
/// counter suffix rather than clobbering the source.
/// </summary>
public static class OutputPathResolver
{
    /// <param name="newExtension">Includes the leading dot, e.g. ".png".</param>
    public static string ResolveDirect(string sourcePath, string newExtension, string outputFolder)
    {
        string stem = Path.GetFileNameWithoutExtension(sourcePath);
        string candidate = Path.Combine(outputFolder, stem + newExtension);

        for (int counter = 1; File.Exists(candidate); counter++)
        {
            candidate = Path.Combine(outputFolder, $"{stem} ({counter}){newExtension}");
        }

        return candidate;
    }
}
