using System.IO;
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
        OverviewDialogAutomationTests.EnsureShellResourcesMerged();
        using var dir = new TempDir();
        using var vm = new WiresharkViewModel(new WiresharkSettingsStore(Path.Combine(dir.Path, "s.json")), new ManualClock());

        var view = new WiresharkView(vm);

        Assert.Same(vm, view.DataContext);
    });
}
