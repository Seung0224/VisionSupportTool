using System.IO;
using System.IO.Compression;
using ICSharpCode.SharpZipLib.Zip;
using SharpZipEntry = ICSharpCode.SharpZipLib.Zip.ZipEntry;
using SharpZipFile = ICSharpCode.SharpZipLib.Zip.ZipFile;

namespace VisionSupport.Archive;

/// <summary>The outcome of one archive job, in the terms the launcher reports it.</summary>
public sealed record ArchiveResult(bool Success, string? OutputPath, string? Error)
{
    public static ArchiveResult Ok(string outputPath) => new(true, outputPath, null);

    public static ArchiveResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// Packing and unpacking, on whichever of two implementations suits the job.
///
/// Without a password it uses System.IO.Compression, which is a native zlib. With one it uses
/// SharpZipLib, which is managed code but can do AES - zlib cannot encrypt at all.
///
/// Having both is not tidiness lost for nothing. Measured on 48 MB: producing the same 18.1 MB
/// archive took the native one 1.3 seconds and the managed one 3.7. Since almost every archive is
/// made without a password, using the managed library for both - which is where this started -
/// made the common case three times slower to save one branch.
///
/// Everything runs off the UI thread and reports whole percentages. Cancellation is checked
/// between entries and inside the copy loop, so a large file does not hold a cancel for minutes.
/// </summary>
public sealed class ZipService
{
    /// <summary>Big enough that the syscall count stops mattering for large files.</summary>
    private const int BufferSize = 1024 * 1024;

    private const int FastLevel = 1;

    private const int SmallLevel = 9;

    /// <summary>
    /// Packs everything dropped into one archive beside it.
    /// </summary>
    /// <param name="password">Null or empty for a plain archive; anything else encrypts with
    /// AES-256, which is what other tools will expect to be asked for.</param>
    public async Task<ArchiveResult> CompressAsync(string[] paths, string? password, bool fast,
                                                   IProgress<int>? progress, CancellationToken ct)
    {
        if (paths.Length == 0) return ArchiveResult.Fail("압축할 항목이 없습니다.");

        string output = string.Empty;

        try
        {
            return await Task.Run(() =>
            {
                output = ArchivePaths.OutputFor(paths);
                List<(string Entry, string File)> entries = Enumerate(paths);

                if (entries.Count == 0) return ArchiveResult.Fail("압축할 파일이 없습니다.");

                long total = Math.Max(1, entries.Sum(e => Length(e.File)));

                if (string.IsNullOrEmpty(password)) PackPlain(output, entries, fast, total, progress, ct);
                else PackEncrypted(output, entries, password, fast, total, progress, ct);

                progress?.Report(100);
                return ArchiveResult.Ok(output);
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Discard(output);
            return ArchiveResult.Fail("압축이 취소되었습니다.");
        }
        catch (Exception ex)
        {
            Discard(output);
            return ArchiveResult.Fail(Describe(ex));
        }
    }

    /// <summary>
    /// Unpacks each archive into a folder of its own beside it.
    /// </summary>
    public async Task<ArchiveResult> ExtractAsync(string[] zipPaths, string? password,
                                                  IProgress<int>? progress, CancellationToken ct)
    {
        if (zipPaths.Length == 0) return ArchiveResult.Fail("압축 해제할 파일이 없습니다.");

        string lastOutput = string.Empty;

        try
        {
            return await Task.Run(() =>
            {
                long total = Math.Max(1, zipPaths.Sum(TotalUncompressed));
                long done = 0;
                int lastPercent = -1;
                int refused = 0;

                foreach (string zipPath in zipPaths)
                {
                    ct.ThrowIfCancellationRequested();

                    string root = ArchivePaths.ExtractionFor(zipPath);
                    Directory.CreateDirectory(root);
                    lastOutput = root;

                    refused += string.IsNullOrEmpty(password)
                        ? UnpackPlain(zipPath, root, total, ref done, ref lastPercent, progress, ct)
                        : UnpackEncrypted(zipPath, root, password, total, ref done, ref lastPercent, progress, ct);
                }

                progress?.Report(100);

                return refused == 0
                    ? ArchiveResult.Ok(lastOutput)
                    : ArchiveResult.Fail(
                        $"압축을 풀었지만 대상 폴더를 벗어나려는 항목 {refused}개를 건너뛰었습니다.");
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArchiveResult.Fail("압축 해제가 취소되었습니다.");
        }
        catch (Exception ex)
        {
            return ArchiveResult.Fail(Describe(ex));
        }
    }


    /// <summary>
    /// The ordinary path: native zlib through System.IO.Compression.
    /// </summary>
    private static void PackPlain(string output, List<(string Entry, string File)> entries, bool fast,
                                  long total, IProgress<int>? progress, CancellationToken ct)
    {
        CompressionLevel level = fast ? CompressionLevel.Fastest : CompressionLevel.Optimal;
        long done = 0;
        int lastPercent = -1;
        byte[] buffer = new byte[BufferSize];

        using var archive = new ZipArchive(File.Create(output), ZipArchiveMode.Create);

        foreach ((string name, string file) in entries)
        {
            ct.ThrowIfCancellationRequested();

            ZipArchiveEntry entry = archive.CreateEntry(name, level);
            entry.LastWriteTime = File.GetLastWriteTime(file);

            using Stream target = entry.Open();
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite, BufferSize);

            Pump(source, target, buffer, ref done, total, ref lastPercent, progress, ct);
        }
    }

    /// <summary>
    /// The encrypted path. zlib cannot lock an archive, so this one is SharpZipLib and pays for it
    /// in speed - which is the right way round, since a password is the rarer request.
    /// </summary>
    private static void PackEncrypted(string output, List<(string Entry, string File)> entries,
                                      string password, bool fast, long total,
                                      IProgress<int>? progress, CancellationToken ct)
    {
        long done = 0;
        int lastPercent = -1;
        byte[] buffer = new byte[BufferSize];

        using var zip = new ZipOutputStream(File.Create(output));
        zip.SetLevel(fast ? FastLevel : SmallLevel);
        zip.Password = password;

        foreach ((string name, string file) in entries)
        {
            ct.ThrowIfCancellationRequested();

            zip.PutNextEntry(new SharpZipEntry(name)
            {
                DateTime = File.GetLastWriteTime(file),
                Size = Length(file),
                AESKeySize = 256,
            });

            using (var source = new FileStream(file, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite, BufferSize))
            {
                Pump(source, zip, buffer, ref done, total, ref lastPercent, progress, ct);
            }

            zip.CloseEntry();
        }
    }

    /// <summary>Unpacks a plain archive. Returns how many entries were refused as unsafe.</summary>
    private static int UnpackPlain(string zipPath, string root, long total, ref long done,
                                   ref int lastPercent, IProgress<int>? progress, CancellationToken ct)
    {
        int refused = 0;
        byte[] buffer = new byte[BufferSize];

        using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            // A directory entry has an empty name after its trailing slash.
            if (entry.Name.Length == 0) continue;

            string? destination = ArchivePaths.SafeEntryPath(root, entry.FullName);
            if (destination is null)
            {
                refused++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            using Stream source = entry.Open();
            using var target = new FileStream(destination, FileMode.Create, FileAccess.Write,
                                              FileShare.None, BufferSize);

            Pump(source, target, buffer, ref done, total, ref lastPercent, progress, ct);
        }

        return refused;
    }

    /// <summary>Unpacks an encrypted archive. Returns how many entries were refused as unsafe.</summary>
    private static int UnpackEncrypted(string zipPath, string root, string password, long total,
                                       ref long done, ref int lastPercent,
                                       IProgress<int>? progress, CancellationToken ct)
    {
        int refused = 0;
        byte[] buffer = new byte[BufferSize];

        using var zip = new SharpZipFile(File.OpenRead(zipPath)) { Password = password };

        foreach (SharpZipEntry entry in zip)
        {
            ct.ThrowIfCancellationRequested();
            if (!entry.IsFile) continue;

            string? destination = ArchivePaths.SafeEntryPath(root, entry.Name);
            if (destination is null)
            {
                refused++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            using Stream source = zip.GetInputStream(entry);
            using var target = new FileStream(destination, FileMode.Create, FileAccess.Write,
                                              FileShare.None, BufferSize);

            Pump(source, target, buffer, ref done, total, ref lastPercent, progress, ct);
        }

        return refused;
    }

    /// <summary>
    /// Copies one stream into another, reporting as it goes. The cancellation check is inside the
    /// loop so a single large file cannot hold a cancel for minutes.
    /// </summary>
    private static void Pump(Stream source, Stream target, byte[] buffer, ref long done, long total,
                             ref int lastPercent, IProgress<int>? progress, CancellationToken ct)
    {
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            target.Write(buffer, 0, read);
            done += read;
            Report(progress, ref lastPercent, done, total);
        }
    }

    /// <summary>
    /// Whether any of these archives will want a password. Asked before unpacking so the prompt
    /// comes up front rather than as a failure part way through.
    /// </summary>
    public static bool AnyRequiresPassword(string[] zipPaths)
    {
        foreach (string path in zipPaths)
        {
            try
            {
                using var zip = new SharpZipFile(File.OpenRead(path));
                foreach (SharpZipEntry entry in zip)
                {
                    if (entry.IsCrypted) return true;
                }
            }
            catch (Exception ex) when (ex is IOException or ZipException or UnauthorizedAccessException)
            {
                // Unreadable here means unreadable later too, and the extract call will say so
                // properly. Guessing "yes" would put a password box in front of a broken file.
            }
        }

        return false;
    }

    /// <summary>
    /// Flattens the drop into archive entries.
    ///
    /// A dropped folder keeps its own name at the top of the archive, so unpacking gives back the
    /// folder rather than spraying its contents into the current directory.
    /// </summary>
    private static List<(string Entry, string File)> Enumerate(string[] paths)
    {
        var entries = new List<(string, string)>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths)
        {
            if (File.Exists(path))
            {
                entries.Add((Unique(Path.GetFileName(path), used), path));
                continue;
            }

            if (!Directory.Exists(path)) continue;

            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string folder = Path.GetFileName(trimmed);
            string parent = Path.GetDirectoryName(trimmed) ?? trimmed;

            foreach (string file in Directory.EnumerateFiles(trimmed, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(parent, file).Replace(Path.DirectorySeparatorChar, '/');
                entries.Add((Unique(relative, used), file));
            }

            if (!Directory.EnumerateFileSystemEntries(trimmed).Any())
            {
                // An empty folder has no files to carry it, so it would vanish from the archive.
                entries.Add((Unique(folder + "/", used), string.Empty));
            }
        }

        return entries.Where(e => e.Item2.Length > 0).ToList();
    }

    private static string Unique(string desired, HashSet<string> used)
    {
        if (used.Add(desired)) return desired;

        string extension = Path.GetExtension(desired);
        string stem = desired[..^extension.Length];

        for (int n = 1; ; n++)
        {
            string candidate = $"{stem} ({n}){extension}";
            if (used.Add(candidate)) return candidate;
        }
    }

    private static long Length(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static long TotalUncompressed(string zipPath)
    {
        try
        {
            using var zip = new SharpZipFile(File.OpenRead(zipPath));
            long total = 0;
            foreach (SharpZipEntry entry in zip)
            {
                if (entry.IsFile && entry.Size > 0) total += entry.Size;
            }

            return total;
        }
        catch (Exception ex) when (ex is IOException or ZipException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void Report(IProgress<int>? progress, ref int last, long done, long total)
    {
        int percent = (int)Math.Min(99, done * 100 / total);
        if (percent == last) return;

        last = percent;
        progress?.Report(percent);
    }

    /// <summary>Removes a half-written archive. A truncated zip looks like a real one.</summary>
    private static void Discard(string path)
    {
        if (path.Length == 0) return;

        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing sensible to do; the message about the original failure is the useful one.
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        ZipException => "압축 파일을 읽을 수 없습니다. 비밀번호가 다르거나 손상된 파일입니다.",
        UnauthorizedAccessException => "권한이 없어 접근할 수 없습니다.",
        DirectoryNotFoundException or FileNotFoundException => "원본을 찾을 수 없습니다.",
        IOException io when io.Message.Contains("space", StringComparison.OrdinalIgnoreCase)
            => "디스크 공간이 부족합니다.",
        _ => ex.Message,
    };
}
