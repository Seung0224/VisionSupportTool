using System.Net;

namespace VisionSupport.Wireshark.Dissect;

[Flags]
public enum TcpFlags : byte { Fin = 1, Syn = 2, Rst = 4, Psh = 8, Ack = 16, Urg = 32 }

public readonly record struct TcpInfo(ushort SrcPort, ushort DstPort, uint Seq, uint Ack,
    TcpFlags Flags, ushort Window, int PayloadLength);

public readonly record struct UdpInfo(ushort SrcPort, ushort DstPort);

public enum AppKind { Mc, Ads, Gvcp }

public enum MessageRole { Request, Response, Unsolicited }

/// <summary>What the application layer said, reduced to what the health tracker needs.</summary>
public sealed record AppMessage(AppKind Kind, MessageRole Role, uint? CorrelationId, string Summary, bool IsError);

public enum GvspFormat : byte { Unknown = 0, Leader = 1, Trailer = 2, Payload = 3, AllIn = 4, H264 = 5, MultiZone = 6 }

public sealed record GvspHeader(ulong BlockId, uint PacketId, GvspFormat Format, ushort Status, bool ExtendedId);

/// <summary>
/// One captured frame and everything read out of it. The list columns are plain strings so the
/// view never re-parses; the typed parts are what the health tracker reads.
/// </summary>
public sealed class Packet
{
    public Packet(long number, DateTime time, byte[] data, int originalLength)
    {
        Number = number;
        Time = time;
        Data = data;
        OriginalLength = originalLength;
    }

    public long Number { get; }

    public DateTime Time { get; }

    /// <summary>The captured bytes - possibly cut short at pktmon's --pkt-size.</summary>
    public byte[] Data { get; }

    /// <summary>The frame's length on the wire.</summary>
    public int OriginalLength { get; }

    public string Source { get; set; } = string.Empty;

    public string Destination { get; set; } = string.Empty;

    public string Protocol { get; set; } = "Ethernet";

    public string Info { get; set; } = string.Empty;

    public IPAddress? SrcIp { get; set; }

    public IPAddress? DstIp { get; set; }

    public TcpInfo? Tcp { get; set; }

    public UdpInfo? Udp { get; set; }

    /// <summary>The first application message in this frame; see <see cref="Messages"/> for all of them.</summary>
    public AppMessage? App { get; set; }

    /// <summary>Every application message in the frame - a PLC may answer several requests in one segment.</summary>
    public List<AppMessage> Messages { get; } = new();

    public void AddMessage(AppMessage message)
    {
        App ??= message;
        Messages.Add(message);
    }

    public GvspHeader? Gvsp { get; set; }

    public List<ProtocolNode> Layers { get; } = new();

    /// <summary>Windows name of the NIC the frame came through ("PLC", "이더넷 2"), when known.</summary>
    public string? Interface { get; set; }

    /// <summary>Which watched target this belongs to; set by the health tracker.</summary>
    public string? TargetId { get; set; }

    /// <summary>Set by the health tracker when this packet is evidence of a problem.</summary>
    public bool IsAnomalous { get; set; }
}
