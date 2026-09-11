using System.IO;

namespace VisionSupport.Archive;

/// <summary>
/// Copy mode: whatever is dropped goes to one folder, unchanged.
///
/// It never moves and never overwrites. The launcher is a target people drag things onto without
/// looking, so the two operations that could lose work are simply not available here - a name
/// that is taken gets "(1)" rather than the file that was already there getting replaced.
/// </summary>
public sealed class CopyService
{
    private const int BufferSize = 1024 * 1024;

    public async Task<ArchiveResult> CopyAsync(string[] paths, string targetFolder,
                                               IProgress<int>? progress, CancellationToken ct)
    {
        if (paths.Length == 0) return ArchiveResult.Fail("복사할 항목이 없습니다.");
        if (string.IsNullOrWhiteSpace(targetFolder)) return ArchiveResult.Fail("복사 대상 폴더가 없습니다.");

        try
        {
            return await Task.Run(() =>
            {
                Directory.CreateDirectory(targetFolder);

                List<(string Source, string Destination)> work = Plan(paths, targetFolder);
                if (work.Count == 0) return ArchiveResult.Fail("복사할 파일이 없습니다.");

                long total = Math.Max(1, work.Sum(w => Length(w.Source)));
                long done = 0;
                int lastPercent = -1;
                byte[] buffer = new byte[BufferSize];

                foreach ((string source, string destination) in work)
                {
                    ct.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                    using var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                                                     FileShare.ReadWrite, BufferSize);
                    using var output = new FileStream(destination, FileMode.CreateNew,
                                                      FileAccess.Write, FileShare.None, BufferSize);

                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        done += read;

                        int percent = (int)Math.Min(99, done * 100 / total);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress?.Report(percent);
                        }
                    }
                }

                progress?.Report(100);
                return ArchiveResult.Ok(targetFolder);
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ArchiveResult.Fail("복사가 취소되었습니다.");
        }
        catch (Exception ex)
        {
            return ArchiveResult.Fail(ex switch
            {
                UnauthorizedAccessException => "권한이 없어 복사할 수 없습니다.",
                DirectoryNotFoundException or FileNotFoundException => "원본을 찾을 수 없습니다.",
                _ => ex.Message,
            });
        }
    }

    /// <summary>
    /// Works out every source-to-destination pair before copying any of them, so a name clash is
    /// resolved against the names this run is about to create as well as the ones already there.
    /// </summary>
    private static List<(string Source, string Destination)> Plan(string[] paths, string target)
    {
        var work = new List<(string, string)>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths)
        {
            if (File.Exists(path))
            {
                work.Add((path, Reserve(target, Path.GetFileName(path), taken)));
                continue;
            }

            if (!Directory.Exists(path)) continue;

            // A folder keeps its shape: the folder itself is placed in the target, and everything
            // under it keeps the same relative layout.
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = ArchivePaths.UniqueDirectory(target, Path.GetFileName(trimmed));

            foreach (string file in Directory.EnumerateFiles(trimmed, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(root, Path.GetRelativePath(trimmed, file));
                taken.Add(destination);
                work.Add((file, destination));
            }
        }

        return work;
    }

    private static string Reserve(string directory, string fileName, HashSet<string> taken)
    {
        string extension = Path.GetExtension(fileName);
        string stem = Path.GetFileNameWithoutExtension(fileName);

        string candidate = Path.Combine(directory, fileName);
        for (int n = 1; File.Exists(candidate) || taken.Contains(candidate); n++)
        {
            candidate = Path.Combine(directory, $"{stem} ({n}){extension}");
        }

        taken.Add(candidate);
        return candidate;
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
}
