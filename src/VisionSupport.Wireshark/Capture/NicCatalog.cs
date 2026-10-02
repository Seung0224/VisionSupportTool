using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace VisionSupport.Wireshark.Capture;

public sealed record NicInfo(int ComponentId, string Mac, string Name, bool IsUp)
{
    public override string ToString() => $"{Name} ({Mac})";
}

/// <summary>
/// pktmon's NIC component ids, joined to Windows' own adapter names by MAC. pktmon tags each
/// packet with its component id; that is the only way to tell which NIC a frame came through.
/// </summary>
public static class NicCatalog
{
    // "  9  00-15-5D-01-02-03  Intel(R) ..." - headers are localised, so only the row shape is matched.
    private static readonly Regex Row = new(
        @"^\s*(\d+)\s+((?:[0-9A-Fa-f]{2}-){5}[0-9A-Fa-f]{2})\s+(.+?)\s*$", RegexOptions.Multiline);

    public static IReadOnlyList<(int Id, string Mac, string Name)> ParseComponentList(string output)
        => Row.Matches(output)
            .Select(m => (int.Parse(m.Groups[1].Value), m.Groups[2].Value.ToUpperInvariant(), m.Groups[3].Value))
            .ToList();

    public static IReadOnlyList<NicInfo> Query()
    {
        (int code, string output) = Pktmon.Run("comp list");
        if (code != 0) throw new InvalidOperationException(output.Trim());

        Dictionary<string, NetworkInterface> byMac = Adapters();
        return ParseComponentList(output)
            .Select(r => byMac.TryGetValue(r.Mac, out NetworkInterface? n)
                ? new NicInfo(r.Id, r.Mac, n.Name, n.OperationalStatus == OperationalStatus.Up)
                : new NicInfo(r.Id, r.Mac, r.Name, false))
            .ToList();
    }

    public static bool? IsUp(string mac)
        => Adapters().TryGetValue(mac, out NetworkInterface? n) ? n.OperationalStatus == OperationalStatus.Up : null;

    private static Dictionary<string, NetworkInterface> Adapters()
    {
        var map = new Dictionary<string, NetworkInterface>(StringComparer.OrdinalIgnoreCase);
        foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
        {
            string mac = string.Join("-", n.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
            if (mac.Length == 17) map.TryAdd(mac, n);
        }
        return map;
    }
}
