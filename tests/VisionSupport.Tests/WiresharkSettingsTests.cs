using System.IO;
using VisionSupport.Wireshark.Health;
using VisionSupport.Wireshark.Settings;
using Xunit;

namespace VisionSupport.Tests;

public class WiresharkSettingsTests
{
    [Fact]
    public void A_missing_file_gives_the_spec_defaults()
    {
        using var dir = new TempDir();
        WiresharkSettings s = new WiresharkSettingsStore(Path.Combine(dir.Path, "settings.json")).Load();

        Assert.Equal(1000, s.Thresholds.ResponseTimeoutMs);
        Assert.Equal(200L * 1024 * 1024, s.StoreMaxBytes);
        Assert.Equal(500_000, s.StoreMaxCount);
        Assert.Equal("Rapixo", s.CxpDeviceNameMatch);
    }

    [Fact]
    public void Pinned_targets_thresholds_and_cxp_nodes_round_trip()
    {
        using var dir = new TempDir();
        var store = new WiresharkSettingsStore(Path.Combine(dir.Path, "sub", "settings.json"));
        var s = new WiresharkSettings();
        s.Pinned.Add(new PinnedTarget { Id = "192.168.0.10:5000", Kind = TargetKind.Mc, Name = "검사기 PLC" });
        s.Thresholds.SilenceSeconds = 9;
        s.CxpNodes.Add(new CxpNodeBinding { Module = "Interface", Node = "LinkStatus", Role = CxpNodeRole.LinkUp, Connection = 1 });

        store.Save(s);
        WiresharkSettings back = store.Load();

        Assert.Equal("검사기 PLC", Assert.Single(back.Pinned).Name);
        Assert.Equal(TargetKind.Mc, back.Pinned[0].Kind);
        Assert.Equal(9, back.Thresholds.SilenceSeconds);
        Assert.Equal(CxpNodeRole.LinkUp, Assert.Single(back.CxpNodes).Role);
    }

    [Fact]
    public void A_corrupt_file_gives_defaults_instead_of_throwing()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{ not json");

        Assert.Empty(new WiresharkSettingsStore(path).Load().Pinned);
    }
}
