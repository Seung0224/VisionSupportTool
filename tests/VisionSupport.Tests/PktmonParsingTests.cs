using VisionSupport.Wireshark.Capture;
using Xunit;

namespace VisionSupport.Tests;

public class PktmonParsingTests
{
    private const string English = """
        Network Adapters:
          Id  MAC Address        Name
          --  -----------        ----
           9  00-15-5D-01-02-03  Intel(R) Ethernet Connection I219-V
          12  A0-B1-C2-D3-E4-F5  Realtek PCIe GbE Family Controller #2

        Virtual Switches:
          Id  Name
          --  ----
          20  Default Switch
        """;

    private const string Korean = """
        네트워크 어댑터:
          ID  MAC 주소           이름
          --  -----------        ----
           9  00-15-5D-01-02-03  Intel(R) Ethernet Connection I219-V
        """;

    [Fact]
    public void Adapter_rows_are_read_and_other_sections_ignored()
    {
        var rows = NicCatalog.ParseComponentList(English);

        Assert.Equal(new[] { 9, 12 }, rows.Select(r => r.Id));
        Assert.Equal("A0-B1-C2-D3-E4-F5", rows[1].Mac);
        Assert.Equal("Realtek PCIe GbE Family Controller #2", rows[1].Name);
    }

    [Fact]
    public void Localised_headers_do_not_matter()
        => Assert.Equal(9, Assert.Single(NicCatalog.ParseComponentList(Korean)).Id);

    [Fact]
    public void Recent_packet_ids_report_a_repeat_once()
    {
        var ids = new RecentPacketIds(capacity: 2);

        Assert.True(ids.Add(1, 1));
        Assert.False(ids.Add(1, 1));
        Assert.True(ids.Add(1, 2));
        Assert.True(ids.Add(1, 3));
        Assert.True(ids.Add(1, 1)); // forgotten: capacity 2
    }
}
