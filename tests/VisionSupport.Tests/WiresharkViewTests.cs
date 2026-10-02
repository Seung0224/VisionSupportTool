using System.IO;
using System.Windows;
using VisionSupport.Wireshark;
using VisionSupport.Wireshark.Settings;
using VisionSupport.Wireshark.ViewModels;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>The view resolves the shell's theme keys at construction; a typo fails here, not on the line PC.</summary>
[Collection(StaAppCollection.Name)]
public class WiresharkViewTests
{
    private readonly StaAppFixture _app;

    public WiresharkViewTests(StaAppFixture app) => _app = app;

    [Fact]
    public void The_view_builds_against_the_shell_theme() => _app.Run(() =>
    {
        ShellTheme.EnsureMerged();
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(new WiresharkSettingsStore(Path.Combine(dir.Path, "s.json")), new ManualClock());

        var view = new WiresharkView(vm);

        Assert.Same(vm, view.DataContext);
    });

    /// <summary>An empty chart still draws its time axis; the label formatter must not run off DateTime.MinValue.</summary>
    [Fact]
    public void The_view_renders_with_an_empty_chart() => _app.Run(() =>
    {
        ShellTheme.EnsureMerged();
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(new WiresharkSettingsStore(Path.Combine(dir.Path, "s.json")), new ManualClock());
        var window = new Window
        {
            Content = new WiresharkView(vm), Width = 1280, Height = 860, Left = -4000, Top = -4000,
            ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None,
        };

        try
        {
            window.Show();
            window.UpdateLayout();
        }
        finally
        {
            window.Close();
        }
    });
}
