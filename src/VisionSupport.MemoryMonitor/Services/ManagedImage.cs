using System.IO;

namespace MemMon.Services;

/// <summary>
/// Tells whether an executable file on disk is a .NET assembly, by reading its PE header.
///
/// This exists for processes we are not allowed to inspect: reading another process's module
/// list needs memory-read rights, which an elevated target denies to a normal process. The
/// file on disk is still readable, and its PE header says whether the image is managed - so a
/// target we cannot open can still be identified and offered, rather than silently hidden.
/// </summary>
public static class ManagedImage
{
    private const int PeSignatureOffsetLocation = 0x3C;
    private const int CoffHeaderSize = 20;
    private const int Pe32Magic = 0x10B;
    private const int Pe32PlusMagic = 0x20B;

    /// <summary>Index of the CLR runtime header in the PE optional header's data directory.</summary>
    private const int ClrDirectoryIndex = 14;

    private const int Pe32DataDirectoryOffset = 96;
    private const int Pe32PlusDataDirectoryOffset = 112;

    public static bool IsManaged(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return false;

        try
        {
            using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);

            if (stream.Length < PeSignatureOffsetLocation + 4) return false;
            stream.Position = PeSignatureOffsetLocation;
            int peHeader = reader.ReadInt32();

            if (peHeader <= 0 || peHeader + 4 + CoffHeaderSize + 2 > stream.Length) return false;
            stream.Position = peHeader;
            if (reader.ReadUInt32() != 0x00004550) return false; // "PE\0\0"

            stream.Position = peHeader + 4 + CoffHeaderSize;
            int magic = reader.ReadUInt16();
            int dataDirectoryOffset = magic switch
            {
                Pe32Magic => Pe32DataDirectoryOffset,
                Pe32PlusMagic => Pe32PlusDataDirectoryOffset,
                _ => -1,
            };
            if (dataDirectoryOffset < 0) return false;

            long clrEntry = peHeader + 4 + CoffHeaderSize + dataDirectoryOffset + (ClrDirectoryIndex * 8);
            if (clrEntry + 8 > stream.Length) return false;

            stream.Position = clrEntry;
            uint rva = reader.ReadUInt32();
            uint size = reader.ReadUInt32();
            return rva != 0 && size != 0;
        }
        catch
        {
            // Unreadable, locked, or not a file we can make sense of - treat as not managed.
            return false;
        }
    }
}
