using VisionSupport.Wireshark.Dissect;
using VisionSupport.Wireshark.Health;
using VisionSupport.Wireshark.ViewModels;
using Xunit;

namespace VisionSupport.Tests;

public class WiresharkViewModelPartsTests
{
    private static Packet Mc() => TestFrames.Dissect(
        TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 5000, TestFrames.McReadRequest3E));

    private static Packet Arp() => TestFrames.Dissect(TestFrames.Arp("192.168.0.2", "192.168.0.10"));

    [Theory]
    [InlineData("", true, true)]
    [InlineData("mc", true, false)]
    [InlineData("!ARP", true, false)]
    [InlineData("192.168.0.10 일괄", true, false)]
    [InlineData("192.168.0.10 RST", false, false)]
    public void Filter_terms_are_and_ed_case_insensitive_and_negatable(string filter, bool mc, bool arp)
    {
        var f = new PacketFilter(filter);

        Assert.Equal(mc, f.Matches(Mc()));
        Assert.Equal(arp, f.Matches(Arp()));
    }

    [Fact]
    public void Filter_by_target_and_by_anomaly()
    {
        Packet p = Mc();
        p.TargetId = "192.168.0.10:5000";
        p.IsAnomalous = true;

        Assert.True(new PacketFilter("id:192.168.0.10:5000").Matches(p));
        Assert.False(new PacketFilter("id:192.168.0.11:5000").Matches(p));
        Assert.True(new PacketFilter("이상").Matches(p));
        Assert.False(new PacketFilter("이상").Matches(Arp()));
    }

    [Fact]
    public void Hex_rows_have_offset_two_groups_of_eight_and_ascii()
    {
        byte[] data = Enumerable.Range(0x41, 18).Select(i => (byte)i).ToArray();

        string[] lines = HexFormatter.Format(data).TrimEnd().Split(Environment.NewLine);

        Assert.Equal("0000  41 42 43 44 45 46 47 48  49 4A 4B 4C 4D 4E 4F 50  ABCDEFGHIJKLMNOP", lines[0]);
        Assert.StartsWith("0010  51 52 ", lines[1]);
        Assert.EndsWith("QR", lines[1]);
    }

    [Fact]
    public void Chart_history_is_capped_and_carries_the_last_response_time_forward()
    {
        var h = new ChartHistory(capacity: 2);
        h.Add(TestFrames.T0, 10, 5);
        h.Add(TestFrames.T0.AddSeconds(1), 20, null);
        h.Add(TestFrames.T0.AddSeconds(2), 30, 7);

        Assert.Equal(new double[] { 20, 30 }, h.BytesPerSecond);
        Assert.Equal(new double[] { 5, 7 }, h.ResponseMs);
    }

    [Fact]
    public void A_card_follows_its_snapshot_and_machine_targets_cannot_be_pinned()
    {
        var card = new TargetCardViewModel("x", TargetKind.Mc);
        card.Update(new TargetSnapshot("x", TargetKind.Mc, "PLC", true, HealthLevel.Bad, "응답 없음 1.0초째", 0, null, 0));

        Assert.Equal(HealthLevel.Bad, card.Level);
        Assert.True(card.Pinned);
        Assert.True(card.CanPin);
        Assert.False(new TargetCardViewModel("nic", TargetKind.Nic).CanPin);
    }

    [Theory]
    [InlineData(TargetKind.Mc, "PLC (MC)")]
    [InlineData(TargetKind.Ads, "PLC (ADS)")]
    [InlineData(TargetKind.GigE, "카메라 (GigE)")]
    [InlineData(TargetKind.Cxp, "카메라 (CXP)")]
    public void A_card_names_what_kind_of_device_it_is(TargetKind kind, string label)
        => Assert.Equal(label, new TargetCardViewModel("x", kind).KindLabel);
}
