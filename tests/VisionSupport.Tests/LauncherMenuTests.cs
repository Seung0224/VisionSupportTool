using System.ComponentModel;
using System.Windows.Controls;
using VisionSupport.Features;
using VisionSupport.Launcher;
using VisionSupport.Shell;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// What the V opens onto. The memory monitor and the PLC server share one tile and a box of their
/// own.
/// </summary>
public class LauncherMenuTests
{
    [Fact]
    public void Memory_monitor_and_plc_share_one_tile()
    {
        LauncherViewModel launcher = Create(new MemoryMonitorFeature(), new PlcServerFeature());

        Assert.Equal(new[] { "도구", "이미지 변환기", "누끼", "메일쓰기", "폴더", "전체보기" },
                     launcher.Items.Select(i => i.Title));
        Assert.Equal(new[] { "메모리 모니터", "PLC 서버" }, launcher.Tools.Select(i => i.Title));
    }

    [Fact]
    public void The_tools_tile_is_ringed_while_a_tool_inside_is_working()
    {
        var idle = new SwitchableFeature();
        var busy = new SwitchableFeature();
        LauncherViewModel launcher = Create(idle, busy);
        LauncherItem tools = launcher.Items.Single(i => i.Title == "도구");
        List<string?> raised = Record(tools);

        Assert.False(tools.IsRunning);

        busy.SetWorking(true);

        Assert.True(tools.IsRunning);
        Assert.Contains(nameof(LauncherItem.IsRunning), raised);
    }

    private static LauncherViewModel Create(IFeatureModule firstTool, IFeatureModule secondTool)
    {
        var settings = new LauncherSettings
        {
            Links = new List<LauncherLink>
            {
                new() { Title = "메일쓰기", Kind = LinkKind.Web, Glyph = "EmailEditOutline", Url = "https://example.invalid" },
                new() { Title = "D", Kind = LinkKind.Folder, Glyph = "FolderOutline", Url = @"D:\" },
            },
        };

        return new LauncherViewModel(
            new IFeatureModule[] { new ImageConverterFeature() },
            new[] { firstTool, secondTool },
            new ActivityLog(), settings, new LauncherAppearance(settings), DateTime.Now);
    }

    private static List<string?> Record(INotifyPropertyChanged source)
    {
        var raised = new List<string?>();
        source.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        return raised;
    }

    private sealed class SwitchableFeature : FeatureModule
    {
        private bool _working;

        public override string Title => "시험 도구";

        public override string Description => "가동 표시 검증용";

        public override bool IsWorking => _working;

        public void SetWorking(bool working)
        {
            _working = working;
            RaiseChanged();
        }

        protected override UserControl CreateView() => new();

        protected override Task OnStopAsync() => Task.CompletedTask;
    }
}
