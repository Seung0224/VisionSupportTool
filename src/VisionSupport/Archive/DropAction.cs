using System.IO;

namespace VisionSupport.Archive;

/// <summary>What dropping something on the launcher should do.</summary>
public enum DropAction
{
    Nothing,

    /// <summary>Copy mode is on: everything goes to the target folder, untouched.</summary>
    Copy,

    /// <summary>Everything dropped was an archive, so unpack rather than pack.</summary>
    Extract,

    Compress,
}

/// <summary>
/// Reads a drop and decides what it means.
///
/// It is three rules in a row and it is worth having on its own, because getting the order wrong
/// is invisible until someone drops a zip while copy mode is on and finds it unpacked instead of
/// filed.
/// </summary>
public static class DropRouter
{
    public static DropAction Decide(IReadOnlyList<string>? paths, bool copyMode)
    {
        if (paths is null || paths.Count == 0) return DropAction.Nothing;

        // Copy mode wins outright. It is a mode the user turned on deliberately, and having it
        // quietly not apply to archives would make it untrustworthy.
        if (copyMode) return DropAction.Copy;

        return AllArchives(paths) ? DropAction.Extract : DropAction.Compress;
    }

    /// <summary>
    /// True when every dropped item is a .zip file. Every - a folder and a zip together is a
    /// compression job, because unpacking cannot say anything about the folder.
    /// </summary>
    public static bool AllArchives(IReadOnlyList<string> paths)
        => paths.Count > 0 && paths.All(p =>
            File.Exists(p) && string.Equals(Path.GetExtension(p), ".zip",
                                            StringComparison.OrdinalIgnoreCase));
}
