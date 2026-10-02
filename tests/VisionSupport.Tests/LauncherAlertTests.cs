using System.Windows.Controls;
using VisionSupport.Features;
using VisionSupport.Launcher;
using VisionSupport.Shell;
using Xunit;

namespace VisionSupport.Tests;

public class LauncherAlertTests
{
    private sealed class AlertingFeature : FeatureModule
    {
        public bool Alert { get; set; }
        public override string Title => "t";
        public override string Description => "d";
        public override bool IsWorking => false;
        public override bool HasAlert => Alert;
        public void Raise() => RaiseChanged();
        protected override UserControl CreateView() => throw new NotSupportedException();
        protected override Task OnStopAsync() => Task.CompletedTask;
    }

    private static LauncherViewModel LauncherWith(IFeatureModule tool)
    {
        var settings = new LauncherSettings();
        return new LauncherViewModel(Array.Empty<IFeatureModule>(), new[] { tool },
            new ActivityLog(), settings, new LauncherAppearance(settings), DateTime.Now);
    }

    [Fact]
    public void A_tool_raising_an_alert_turns_the_launcher_ring_red()
    {
        var tool = new AlertingFeature();
        LauncherViewModel launcher = LauncherWith(tool);
        var raised = new List<string?>();
        launcher.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.False(launcher.AnyAlert);
        tool.Alert = true;
        tool.Raise();

        Assert.True(launcher.AnyAlert);
        Assert.Contains(nameof(LauncherViewModel.AnyAlert), raised);
    }

    [Fact]
    public void Features_that_never_alert_default_to_false()
        => Assert.False(new MemoryMonitorFeature().HasAlert);
}
