using System.IO;

namespace VisionSupport.Archive;

/// <summary>
/// Where an archive goes, and where its contents land.
///
/// All of it is string and file-system arithmetic with no compression in sight, which is the
/// point: these are the decisions that quietly overwrite someone's work if they are wrong, so
/// they are separated out where they can be tested.
/// </summary>
public static class ArchivePaths
{
    /// <summary>
    /// The .zip to create for a drop: beside what was dropped, named after it.
    ///
    /// Several items at once have no shared name to take, so they become "Archive" in the first
    /// one's folder - the same folder the user dragged them out of.
    /// </summary>
    public static string OutputFor(IReadOnlyList<string> paths)
    {
        string first = Trim(paths[0]);
        string directory = Path.GetDirectoryName(first)
                           ?? Path.GetPathRoot(first)
                           ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        string baseName = paths.Count > 1
            ? "Archive"
            : Directory.Exists(first) ? Path.GetFileName(first) : Path.GetFileNameWithoutExtension(first);

        return UniqueFile(directory, baseName, ".zip");
    }

    /// <summary>The folder an archive unpacks into: beside it, named after it.</summary>
    public static string ExtractionFor(string zipPath)
    {
        string directory = Path.GetDirectoryName(zipPath)
                           ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        return UniqueDirectory(directory, Path.GetFileNameWithoutExtension(zipPath));
    }

    /// <summary>
    /// Adds "(1)", "(2)" until the name is free. Never returns a path that already exists: an
    /// archive that silently replaced last week's is the worst thing this code could do.
    /// </summary>
    public static string UniqueFile(string directory, string baseName, string extension)
    {
        string candidate = Path.Combine(directory, baseName + extension);
        if (!File.Exists(candidate)) return candidate;

        for (int n = 1; ; n++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({n}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    public static string UniqueDirectory(string parent, string baseName)
    {
        string candidate = Path.Combine(parent, baseName);
        if (!Directory.Exists(candidate)) return candidate;

        for (int n = 1; ; n++)
        {
            candidate = Path.Combine(parent, $"{baseName} ({n})");
            if (!Directory.Exists(candidate)) return candidate;
        }
    }

    /// <summary>
    /// Resolves one archive entry against the folder it is being unpacked into, and refuses
    /// anything that lands outside it.
    ///
    /// An archive is a list of names supplied by whoever built it, and a name like
    /// "..\..\Windows\System32\x.dll" is a perfectly legal one. Left unchecked it writes exactly
    /// where it says - the flaw known as zip slip. Nothing stops an archive from containing that;
    /// this is what stops it from mattering.
    /// </summary>
    /// <returns>The full path to write to, or null if the entry escapes the destination.</returns>
    public static string? SafeEntryPath(string destinationRoot, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)) return null;

        // Archives use forward slashes; Windows needs its own, and a rooted entry name would
        // otherwise ignore the destination entirely.
        string relative = entryName.Replace('/', Path.DirectorySeparatorChar).TrimStart(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (relative.Length == 0 || Path.IsPathRooted(relative)) return null;

        string root = Path.GetFullPath(destinationRoot);
        string full = Path.GetFullPath(Path.Combine(root, relative));

        // The separator matters: without it "C:\out" would also accept "C:\output-elsewhere".
        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static string Trim(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
