using System.Collections.Concurrent;
using System.Net;

namespace VisionSupport.Wireshark.Dissect;

/// <summary>
/// What the dissector needs to know beyond the bytes: which ports mean MC, and which hosts are
/// GigE cameras. GVSP has no fixed port, so a UDP datagram is only read as video once its sender
/// has answered on GVCP - that is how cameras get learned.
/// </summary>
public sealed class DissectorContext
{
    public const int GvcpPort = 3956;

    private readonly ConcurrentDictionary<IPAddress, byte> _cameras = new();

    public int McPortMin { get; init; } = 5000;

    public int McPortMax { get; init; } = 5010;

    public int AdsPort { get; init; } = 48898;

    public bool IsMcPort(int port) => port >= McPortMin && port <= McPortMax;

    public void LearnCamera(IPAddress address) => _cameras.TryAdd(address, 0);

    public bool IsCamera(IPAddress address) => _cameras.ContainsKey(address);
}
