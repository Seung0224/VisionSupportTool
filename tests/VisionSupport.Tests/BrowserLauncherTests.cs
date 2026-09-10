using VisionSupport.Launcher;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The parts of opening a browser that are decidable without one. Duplicating the shell's token
/// and creating a process need a real desktop, so they are left to the smoke pass; picking the
/// executable out of a registry command and building the command line do not.
/// </summary>
public class BrowserLauncherTests
{
    [Theory]
    [InlineData("\"C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe\" --single-argument %1",
                "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe")]
    [InlineData("\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" -- \"%1\"",
                "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe")]
    [InlineData("C:\\Tools\\browser.exe %1", "C:\\Tools\\browser.exe")]
    [InlineData("C:\\Tools\\browser.exe", "C:\\Tools\\browser.exe")]
    public void Reads_the_executable_out_of_a_registry_open_command(string command, string expected)
        => Assert.Equal(expected, BrowserLauncher.ExtractExecutable(command));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"unterminated")]
    public void Refuses_a_command_it_cannot_read(string? command)
        => Assert.Null(BrowserLauncher.ExtractExecutable(command));

    [Theory]
    [InlineData("C:\\x\\msedge.exe", true)]
    [InlineData("C:\\x\\CHROME.EXE", true)]
    [InlineData("C:\\x\\brave.exe", true)]
    [InlineData("C:\\x\\firefox.exe", false)]
    [InlineData("", false)]
    public void Knows_which_browsers_can_open_an_app_window(string path, bool expected)
        => Assert.Equal(expected, BrowserLauncher.SupportsAppWindow(path));

    [Fact]
    public void Quotes_the_url_so_a_query_string_survives_the_command_line()
    {
        string arguments = BrowserLauncher.BuildPopupArguments(
            "https://example.com/a.do?kind=plain&x=1", 1100, 800);

        Assert.Equal("--app=\"https://example.com/a.do?kind=plain&x=1\" --window-size=1100,800",
                     arguments);
    }
}
