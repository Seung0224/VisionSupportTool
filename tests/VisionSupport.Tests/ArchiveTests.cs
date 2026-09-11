using System.IO;
using VisionSupport.Archive;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Where an archive lands, and what an archive is allowed to write.
///
/// These are the parts of packing and unpacking that can destroy something. Compression itself
/// either works or throws; a wrong output path silently replaces last week's archive, and an
/// unchecked entry name writes wherever it likes.
/// </summary>
public class ArchivePathTests
{
    [Fact]
    public void An_archive_is_named_after_the_folder_and_sits_beside_it()
    {
        using var dir = new TempDir();
        string source = Path.Combine(dir.Path, "프로젝트");
        Directory.CreateDirectory(source);

        string output = ArchivePaths.OutputFor(new[] { source });

        Assert.Equal(Path.Combine(dir.Path, "프로젝트.zip"), output);
    }

    [Fact]
    public void A_file_loses_its_extension_rather_than_gaining_one()
    {
        using var dir = new TempDir();
        string source = Path.Combine(dir.Path, "보고서.docx");
        File.WriteAllText(source, "x");

        Assert.Equal(Path.Combine(dir.Path, "보고서.zip"), ArchivePaths.OutputFor(new[] { source }));
    }

    [Fact]
    public void Several_items_share_one_archive_in_the_first_one_s_folder()
    {
        using var dir = new TempDir();
        string a = Path.Combine(dir.Path, "a.txt");
        string b = Path.Combine(dir.Path, "b.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");

        Assert.Equal(Path.Combine(dir.Path, "Archive.zip"), ArchivePaths.OutputFor(new[] { a, b }));
    }

    [Fact]
    public void An_existing_archive_is_never_written_over()
    {
        using var dir = new TempDir();
        string source = Path.Combine(dir.Path, "데이터");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(dir.Path, "데이터.zip"), "already here");

        Assert.Equal(Path.Combine(dir.Path, "데이터 (1).zip"), ArchivePaths.OutputFor(new[] { source }));
    }

    [Fact]
    public void An_archive_unpacks_into_a_folder_of_its_own_name()
    {
        using var dir = new TempDir();
        string zip = Path.Combine(dir.Path, "받은자료.zip");

        Assert.Equal(Path.Combine(dir.Path, "받은자료"), ArchivePaths.ExtractionFor(zip));
    }

    [Fact]
    public void Unpacking_twice_does_not_merge_into_the_first_folder()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "받은자료"));

        Assert.Equal(Path.Combine(dir.Path, "받은자료 (1)"),
                     ArchivePaths.ExtractionFor(Path.Combine(dir.Path, "받은자료.zip")));
    }
}

/// <summary>
/// The zip-slip guard. An archive's entry names come from whoever built it, so a name like
/// "..\..\Windows\System32\x.dll" is legal and, unchecked, writes exactly there.
/// </summary>
public class ArchiveEntrySafetyTests
{
    [Theory]
    [InlineData("readme.txt")]
    [InlineData("docs/readme.txt")]
    [InlineData("깊은/폴더/구조/파일.bin")]
    // A leading slash means the root of the archive, not the root of the disk. Every extractor
    // strips it, which lands the entry inside the destination rather than outside it.
    [InlineData("/absolute/from/root.txt")]
    public void An_ordinary_entry_lands_inside_the_destination(string entry)
    {
        string? resolved = ArchivePaths.SafeEntryPath(@"C:\out", entry);

        Assert.NotNull(resolved);
        Assert.StartsWith(@"C:\out\", resolved);
    }

    [Theory]
    [InlineData(@"..\escape.txt")]
    [InlineData("../escape.txt")]
    [InlineData("docs/../../escape.txt")]
    [InlineData(@"C:\Windows\System32\evil.dll")]
    [InlineData("")]
    [InlineData("   ")]
    public void An_entry_that_reaches_outside_is_refused(string entry)
        => Assert.Null(ArchivePaths.SafeEntryPath(@"C:\out", entry));

    /// <summary>
    /// The separator in the comparison is what makes this fail rather than pass: "C:\out" is a
    /// prefix of "C:\output" as a string, but not as a folder.
    /// </summary>
    [Fact]
    public void A_sibling_folder_with_a_longer_name_is_not_inside()
        => Assert.Null(ArchivePaths.SafeEntryPath(@"C:\out", @"..\output\sneaky.txt"));
}

/// <summary>Three rules in a row, and the order of them is the whole behaviour.</summary>
public class DropRouterTests
{
    [Fact]
    public void Nothing_dropped_does_nothing()
    {
        Assert.Equal(DropAction.Nothing, DropRouter.Decide(null, copyMode: false));
        Assert.Equal(DropAction.Nothing, DropRouter.Decide(Array.Empty<string>(), copyMode: false));
    }

    [Fact]
    public void Copy_mode_takes_precedence_even_over_archives()
    {
        using var dir = new TempDir();
        string zip = Path.Combine(dir.Path, "a.zip");
        File.WriteAllText(zip, "x");

        Assert.Equal(DropAction.Copy, DropRouter.Decide(new[] { zip }, copyMode: true));
    }

    [Fact]
    public void Archives_alone_are_unpacked()
    {
        using var dir = new TempDir();
        string zip = Path.Combine(dir.Path, "a.ZIP");
        File.WriteAllText(zip, "x");

        Assert.Equal(DropAction.Extract, DropRouter.Decide(new[] { zip }, copyMode: false));
    }

    /// <summary>
    /// One archive among other things is a packing job. Unpacking has nothing to say about the
    /// folder that came with it.
    /// </summary>
    [Fact]
    public void An_archive_mixed_with_anything_else_is_packed()
    {
        using var dir = new TempDir();
        string zip = Path.Combine(dir.Path, "a.zip");
        string folder = Path.Combine(dir.Path, "b");
        File.WriteAllText(zip, "x");
        Directory.CreateDirectory(folder);

        Assert.Equal(DropAction.Compress, DropRouter.Decide(new[] { zip, folder }, copyMode: false));
    }

    [Fact]
    public void A_folder_named_like_an_archive_is_not_one()
    {
        using var dir = new TempDir();
        string folder = Path.Combine(dir.Path, "looks.zip");
        Directory.CreateDirectory(folder);

        Assert.Equal(DropAction.Compress, DropRouter.Decide(new[] { folder }, copyMode: false));
    }
}
