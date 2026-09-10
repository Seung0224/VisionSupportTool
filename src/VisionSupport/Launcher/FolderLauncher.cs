using System.Diagnostics;
using System.IO;

namespace VisionSupport.Launcher;

/// <summary>
/// Opens a folder in Explorer.
///
/// Explorer is asked by name rather than through ShellExecute, and that is deliberate: this
/// process runs elevated, and a shell window opened by an elevated process is itself elevated -
/// a different set of mapped drives, a different recent-files list, and drag-and-drop out of it
/// into ordinary programs stops working. Starting explorer.exe hands the request to the shell
/// that is already running as the user, which opens the window where it belongs.
/// </summary>
public static class FolderLauncher
{
    /// <summary>
    /// Opens <paramref name="path"/>, or reports why not.
    ///
    /// The path is checked first because Explorer's answer to an argument it cannot make sense of
    /// is to open a window on Documents - a shortcut that silently goes somewhere else is worse
    /// than one that says it is broken.
    /// </summary>
    public static Result Open(string? path)
    {
        // An unset tile opens Documents rather than refusing. Explorer with no argument lands on
        // Quick Access, which is a different place on every machine; Documents is somewhere.
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        if (!Directory.Exists(path)) return Result.Missing;

        try
        {
            // Quoted: a path with spaces would otherwise arrive as several arguments, and
            // Explorer would open the first thing that happened to parse.
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            {
                UseShellExecute = false,
            });

            return Result.Opened;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Result.Failed;
        }
    }

    public enum Result
    {
        Opened,

        /// <summary>The path is set but there is nothing there - an unmapped drive, a renamed
        /// folder, a server that is off.</summary>
        Missing,

        Failed,
    }
}
