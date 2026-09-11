using System.IO;
using VisionSupport.Archive;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Packing and unpacking for real, on small files.
///
/// A round trip is the only check worth making here: the failure that matters is not "did it
/// throw" but "did everything come back", and folder structure and encryption are both easy to
/// get subtly wrong in ways that only show up on the way out.
/// </summary>
public class ZipServiceTests
{
    [Fact]
    public async Task A_folder_survives_a_round_trip_with_its_structure()
    {
        using var dir = new TempDir();
        string source = Path.Combine(dir.Path, "원본");
        Directory.CreateDirectory(Path.Combine(source, "하위"));
        File.WriteAllText(Path.Combine(source, "위.txt"), "top");
        File.WriteAllText(Path.Combine(source, "하위", "아래.txt"), "nested");

        var service = new ZipService();

        ArchiveResult packed = await service.CompressAsync(
            new[] { source }, password: null, fast: true, null, CancellationToken.None);

        Assert.True(packed.Success, packed.Error);
        Assert.True(File.Exists(packed.OutputPath));

        // Unpack somewhere the source is not, so nothing can pass by finding the original.
        string archive = Path.Combine(dir.Path, "격리.zip");
        File.Move(packed.OutputPath!, archive);
        Directory.Delete(source, recursive: true);

        ArchiveResult unpacked = await service.ExtractAsync(
            new[] { archive }, password: null, null, CancellationToken.None);

        Assert.True(unpacked.Success, unpacked.Error);
        Assert.Equal("top", File.ReadAllText(Path.Combine(unpacked.OutputPath!, "원본", "위.txt")));
        Assert.Equal("nested", File.ReadAllText(Path.Combine(unpacked.OutputPath!, "원본", "하위", "아래.txt")));
    }

    [Fact]
    public async Task A_password_locks_the_archive_and_opens_it_again()
    {
        using var dir = new TempDir();
        string file = Path.Combine(dir.Path, "비밀.txt");
        File.WriteAllText(file, "secret");

        var service = new ZipService();

        ArchiveResult packed = await service.CompressAsync(
            new[] { file }, "열려라참깨", fast: true, null, CancellationToken.None);

        Assert.True(packed.Success, packed.Error);
        Assert.True(ZipService.AnyRequiresPassword(new[] { packed.OutputPath! }));

        ArchiveResult unpacked = await service.ExtractAsync(
            new[] { packed.OutputPath! }, "열려라참깨", null, CancellationToken.None);

        Assert.True(unpacked.Success, unpacked.Error);
        Assert.Equal("secret", File.ReadAllText(Path.Combine(unpacked.OutputPath!, "비밀.txt")));
    }

    [Fact]
    public async Task The_wrong_password_fails_rather_than_writing_rubbish()
    {
        using var dir = new TempDir();
        string file = Path.Combine(dir.Path, "비밀.txt");
        File.WriteAllText(file, "secret");

        var service = new ZipService();
        ArchiveResult packed = await service.CompressAsync(
            new[] { file }, "맞는비밀번호", fast: true, null, CancellationToken.None);

        ArchiveResult unpacked = await service.ExtractAsync(
            new[] { packed.OutputPath! }, "틀린비밀번호", null, CancellationToken.None);

        Assert.False(unpacked.Success);
    }

    [Fact]
    public async Task A_plain_archive_is_not_reported_as_needing_a_password()
    {
        using var dir = new TempDir();
        string file = Path.Combine(dir.Path, "공개.txt");
        File.WriteAllText(file, "open");

        ArchiveResult packed = await new ZipService().CompressAsync(
            new[] { file }, password: null, fast: true, null, CancellationToken.None);

        Assert.False(ZipService.AnyRequiresPassword(new[] { packed.OutputPath! }));
    }

    [Fact]
    public async Task Progress_ends_at_a_hundred()
    {
        using var dir = new TempDir();
        string file = Path.Combine(dir.Path, "진행.bin");
        File.WriteAllBytes(file, new byte[512 * 1024]);

        var reported = new List<int>();
        var progress = new Progress<int>(reported.Add);

        ArchiveResult packed = await new ZipService().CompressAsync(
            new[] { file }, password: null, fast: true, progress, CancellationToken.None);

        Assert.True(packed.Success, packed.Error);

        // Progress<T> posts to the captured context; without one it lands on the pool, so give
        // the last report a moment to arrive before reading the list.
        await Task.Delay(200);
        Assert.Contains(100, reported);
    }
}

/// <summary>Copy mode: it files things, and it never destroys anything doing so.</summary>
public class CopyServiceTests
{
    [Fact]
    public async Task Files_land_in_the_target_folder()
    {
        using var dir = new TempDir();
        string source = Path.Combine(dir.Path, "원본.txt");
        string target = Path.Combine(dir.Path, "대상");
        File.WriteAllText(source, "content");

        ArchiveResult result = await new CopyService().CopyAsync(
            new[] { source }, target, null, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal("content", File.ReadAllText(Path.Combine(target, "원본.txt")));
        Assert.True(File.Exists(source), "원본이 사라졌습니다 — 복사는 이동이 아닙니다.");
    }

    [Fact]
    public async Task An_existing_file_is_kept_and_the_copy_is_renamed()
    {
        using var dir = new TempDir();
        string source = Path.Combine(dir.Path, "원본.txt");
        string target = Path.Combine(dir.Path, "대상");
        Directory.CreateDirectory(target);
        File.WriteAllText(source, "new");
        File.WriteAllText(Path.Combine(target, "원본.txt"), "already here");

        ArchiveResult result = await new CopyService().CopyAsync(
            new[] { source }, target, null, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal("already here", File.ReadAllText(Path.Combine(target, "원본.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "원본 (1).txt")));
    }

    [Fact]
    public async Task A_folder_keeps_its_shape()
    {
        using var dir = new TempDir();
        string source = Path.Combine(dir.Path, "묶음");
        Directory.CreateDirectory(Path.Combine(source, "안쪽"));
        File.WriteAllText(Path.Combine(source, "안쪽", "파일.txt"), "deep");
        string target = Path.Combine(dir.Path, "대상");

        ArchiveResult result = await new CopyService().CopyAsync(
            new[] { source }, target, null, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal("deep", File.ReadAllText(Path.Combine(target, "묶음", "안쪽", "파일.txt")));
    }
}
