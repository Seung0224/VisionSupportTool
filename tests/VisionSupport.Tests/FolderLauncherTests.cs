using System.IO;
using VisionSupport.Launcher;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The check before the shell is asked to open anything.
///
/// Explorer's answer to an argument it cannot make sense of is a window on Documents, which is
/// how a broken shortcut turns into a shortcut that quietly goes somewhere else. These are the
/// cases that must never reach it.
/// </summary>
public class FolderLauncherTests
{
    /// <summary>
    /// A tile with nothing set opens Documents. Explorer with no argument at all lands on Quick
    /// Access, which is somewhere different on every machine and often nowhere useful.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unset_path_falls_back_to_documents(string? path)
        => Assert.Equal(FolderLauncher.Result.Opened, FolderLauncher.Open(path));

    [Fact]
    public void A_path_that_is_not_there_is_reported_rather_than_opened()
        => Assert.Equal(FolderLauncher.Result.Missing,
            FolderLauncher.Open(@"Z:\definitely\not\here\vision"));

    [Fact]
    public void A_file_is_not_a_folder()
    {
        using var dir = new TempDir();
        string file = Path.Combine(dir.Path, "note.txt");
        File.WriteAllText(file, "x");

        Assert.Equal(FolderLauncher.Result.Missing, FolderLauncher.Open(file));
    }
}
