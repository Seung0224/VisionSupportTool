using System.Buffers.Binary;
using System.Net;
using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Tests;

/// <summary>Builds Ethernet/IPv4 frames byte by byte, so dissector tests read like the wire.</summary>
internal static class TestFrames
{
    public static readonly DateTime T0 = new(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);

    public static byte[] Tcp(string src, int srcPort, string dst, int dstPort, byte[]? payload = null,
        uint seq = 1000, uint ack = 1, TcpFlags flags = TcpFlags.Ack | TcpFlags.Psh, ushort window = 8192)
    {
        payload ??= Array.Empty<byte>();
        var tcp = new byte[20 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(0), (ushort)srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(2), (ushort)dstPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.AsSpan(4), seq);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.AsSpan(8), ack);
        tcp[12] = 5 << 4;
        tcp[13] = (byte)flags;
        BinaryPrimitives.WriteUInt16BigEndian(tcp.AsSpan(14), window);
        payload.CopyTo(tcp, 20);
        return Ip(src, dst, 6, tcp);
    }

    public static byte[] Udp(string src, int srcPort, string dst, int dstPort, byte[] payload)
    {
        var udp = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(udp.AsSpan(0), (ushort)srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp.AsSpan(2), (ushort)dstPort);
        BinaryPrimitives.WriteUInt16BigEndian(udp.AsSpan(4), (ushort)udp.Length);
        payload.CopyTo(udp, 8);
        return Ip(src, dst, 17, udp);
    }

    public static byte[] Arp(string senderIp, string targetIp)
    {
        var frame = new byte[14 + 28];
        for (int i = 0; i < 6; i++) { frame[i] = 0xFF; frame[6 + i] = 0x22; }
        frame[12] = 0x08; frame[13] = 0x06;
        Span<byte> arp = frame.AsSpan(14);
        arp[1] = 1; arp[2] = 0x08; arp[4] = 6; arp[5] = 4; arp[7] = 1; // request
        IPAddress.Parse(senderIp).GetAddressBytes().CopyTo(arp[14..]);
        IPAddress.Parse(targetIp).GetAddressBytes().CopyTo(arp[24..]);
        return frame;
    }

    private static byte[] Ip(string src, string dst, byte protocol, byte[] l4)
    {
        var frame = new byte[14 + 20 + l4.Length];
        for (int i = 0; i < 6; i++) { frame[i] = 0x11; frame[6 + i] = 0x22; }
        frame[12] = 0x08; frame[13] = 0x00;
        Span<byte> ip = frame.AsSpan(14);
        ip[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(ip[2..], (ushort)(20 + l4.Length));
        ip[8] = 64;
        ip[9] = protocol;
        IPAddress.Parse(src).GetAddressBytes().CopyTo(ip[12..]);
        IPAddress.Parse(dst).GetAddressBytes().CopyTo(ip[16..]);
        l4.CopyTo(ip[20..]);
        return frame;
    }

    public static Packet Dissect(byte[] frame, DissectorContext? context = null, long number = 1,
        DateTime? time = null, int? originalLength = null)
        => FrameDissector.Dissect(number, time ?? T0, frame, originalLength ?? frame.Length,
            context ?? new DissectorContext());

    // D100부터 10워드 일괄 읽기 (0401/0000), 3E 바이너리.
    public static readonly byte[] McReadRequest3E =
    {
        0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x0C, 0x00, 0x10, 0x00,
        0x01, 0x04, 0x00, 0x00, 0x64, 0x00, 0x00, 0xA8, 0x0A, 0x00,
    };

    public static readonly byte[] McOkResponse3E =
        { 0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x02, 0x00, 0x00, 0x00 };

    public static readonly byte[] McErrorResponse3E =
        { 0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x02, 0x00, 0x59, 0xC0 };

    public static byte[] McReadRequest4E(ushort serial)
        => new byte[] { 0x54, 0x00, (byte)serial, (byte)(serial >> 8), 0x00, 0x00 }
            .Concat(McReadRequest3E.Skip(2)).ToArray();

    public static byte[] McOkResponse4E(ushort serial)
        => new byte[] { 0xD4, 0x00, (byte)serial, (byte)(serial >> 8), 0x00, 0x00 }
            .Concat(McOkResponse3E.Skip(2)).ToArray();
}
