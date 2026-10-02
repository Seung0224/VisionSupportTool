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

    /// <summary>AMS/TCP header + AMS header (+ 4-byte ADS result on responses).</summary>
    public static byte[] Ads(ushort command, bool response, uint invokeId, uint error = 0, uint result = 0)
    {
        int data = response ? 4 : 0;
        var p = new byte[6 + 32 + data];
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), (uint)(32 + data));
        byte[] target = { 5, 1, 2, 3, 1, 1 }, source = { 192, 168, 0, 2, 1, 1 };
        (response ? source : target).CopyTo(p, 6);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(12), (ushort)(response ? 30000 : 851));
        (response ? target : source).CopyTo(p, 14);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(20), (ushort)(response ? 851 : 30000));
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(22), command);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(24), (ushort)(response ? 0x0005 : 0x0004));
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(26), (uint)data);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(30), error);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(34), invokeId);
        if (response) BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(38), result);
        return p;
    }

    public const string Camera = "192.168.1.20";
    public const string Host = "192.168.1.2";

    public static byte[] GvcpReadRegCmd(ushort reqId)
    {
        var p = new byte[12];
        p[0] = 0x42; p[1] = 0x01;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2), 0x0080);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(4), 4);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(6), reqId);
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(8), 0x0A00);
        return Udp(Host, 50001, Camera, 3956, p);
    }

    public static byte[] GvcpReadRegAck(ushort ackId, ushort status = 0)
    {
        var p = new byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0), status);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2), 0x0081);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(4), 4);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(6), ackId);
        return Udp(Camera, 3956, Host, 50001, p);
    }

    public static byte[] Gvsp(ushort block, uint packetId, GvspFormat format)
    {
        var p = new byte[8 + 16];
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2), block);
        p[4] = (byte)format;
        p[5] = (byte)(packetId >> 16); p[6] = (byte)(packetId >> 8); p[7] = (byte)packetId;
        return Udp(Camera, 20202, Host, 50010, p);
    }

    public static byte[] GvspExtended(ulong block, uint packetId, GvspFormat format)
    {
        var p = new byte[20 + 16];
        p[4] = (byte)(0x80 | (byte)format);
        BinaryPrimitives.WriteUInt64BigEndian(p.AsSpan(8), block);
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(16), packetId);
        return Udp(Camera, 20202, Host, 50010, p);
    }
}
