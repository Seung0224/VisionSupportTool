using System.Buffers.Binary;
using System.Net;

namespace VisionSupport.Wireshark.Dissect;

/// <summary>
/// Bytes to <see cref="Packet"/>. Runs on the capture thread for every frame, so it never throws:
/// a frame it cannot read becomes a row that says so.
/// </summary>
public static class FrameDissector
{
    public static Packet Dissect(long number, DateTime time, byte[] frame, int originalLength, DissectorContext context)
    {
        var packet = new Packet(number, time, frame, originalLength);
        try
        {
            DissectEthernet(packet, context);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException)
        {
            packet.Info = "해석 실패: " + ex.GetType().Name;
        }
        return packet;
    }

    private static void DissectEthernet(Packet packet, DissectorContext context)
    {
        byte[] f = packet.Data;
        if (f.Length < 14)
        {
            packet.Info = $"잘린 프레임 ({f.Length} 바이트)";
            return;
        }

        int offset = 12;
        ushort type = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(offset));
        if (type == 0x8100 && f.Length >= 18)
        {
            offset += 4;
            type = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(offset));
        }
        int l3 = offset + 2;

        string dstMac = Mac(f.AsSpan(0, 6)), srcMac = Mac(f.AsSpan(6, 6));
        packet.Source = srcMac;
        packet.Destination = dstMac;
        var eth = new ProtocolNode($"Ethernet II, {srcMac} → {dstMac}", 0, l3);
        eth.Add($"유형: 0x{type:X4}", offset, 2);
        packet.Layers.Add(eth);

        switch (type)
        {
            case 0x0806: DissectArp(packet, l3); break;
            case 0x0800: DissectIPv4(packet, l3, context); break;
            default: packet.Info = $"EtherType 0x{type:X4}"; break;
        }
    }

    private static void DissectArp(Packet packet, int at)
    {
        byte[] f = packet.Data;
        packet.Protocol = "ARP";
        if (f.Length < at + 28)
        {
            packet.Info = "ARP (잘림)";
            return;
        }

        ushort op = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 6));
        var sender = new IPAddress(f.AsSpan(at + 14, 4));
        var target = new IPAddress(f.AsSpan(at + 24, 4));
        packet.Info = op == 1
            ? $"{target} 은(는) 누구? {sender} 에게 알려줘"
            : $"{sender} 은(는) {Mac(f.AsSpan(at + 8, 6))}";
        var node = new ProtocolNode($"ARP, {(op == 1 ? "요청" : "응답")}", at, 28);
        node.Add($"보낸 쪽: {sender}", at + 14, 4);
        node.Add($"대상: {target}", at + 24, 4);
        packet.Layers.Add(node);
    }

    private static void DissectIPv4(Packet packet, int at, DissectorContext context)
    {
        byte[] f = packet.Data;
        packet.Protocol = "IPv4";
        if (f.Length < at + 20)
        {
            packet.Info = "IPv4 (잘림)";
            return;
        }

        int headerLength = (f[at] & 0x0F) * 4;
        int totalLength = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 2));
        ushort fragment = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 6));
        byte protocol = f[at + 9];
        var src = new IPAddress(f.AsSpan(at + 12, 4));
        var dst = new IPAddress(f.AsSpan(at + 16, 4));
        packet.SrcIp = src;
        packet.DstIp = dst;
        packet.Source = src.ToString();
        packet.Destination = dst.ToString();

        var node = new ProtocolNode($"IPv4, {src} → {dst}", at, headerLength);
        node.Add($"전체 길이: {totalLength}", at + 2, 2);
        node.Add($"프로토콜: {protocol}", at + 9, 1);
        packet.Layers.Add(node);

        if ((fragment & 0x1FFF) != 0 || headerLength < 20)
        {
            packet.Info = "IPv4 조각";
            return;
        }

        int l4 = at + headerLength;
        // What the sender put on the wire after the IP header, not what pktmon kept of it.
        int l4Length = Math.Max(0, totalLength - headerLength);
        switch (protocol)
        {
            case 6: DissectTcp(packet, l4, l4Length, context); break;
            case 17: DissectUdp(packet, l4, context); break;
            default: packet.Info = $"IP 프로토콜 {protocol}"; break;
        }
    }

    private static void DissectTcp(Packet packet, int at, int segmentLength, DissectorContext context)
    {
        byte[] f = packet.Data;
        packet.Protocol = "TCP";
        if (f.Length < at + 20)
        {
            packet.Info = "TCP (잘림)";
            return;
        }

        ushort sp = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at));
        ushort dp = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 2));
        uint seq = BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(at + 4));
        uint ack = BinaryPrimitives.ReadUInt32BigEndian(f.AsSpan(at + 8));
        int headerLength = (f[at + 12] >> 4) * 4;
        var flags = (TcpFlags)(f[at + 13] & 0x3F);
        ushort window = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 14));
        int payloadLength = Math.Max(0, segmentLength - headerLength);

        packet.Tcp = new TcpInfo(sp, dp, seq, ack, flags, window, payloadLength);
        packet.Source = $"{packet.SrcIp}:{sp}";
        packet.Destination = $"{packet.DstIp}:{dp}";
        packet.Info = $"{sp} → {dp} [{FlagText(flags)}] Seq={seq} Ack={ack} Win={window} Len={payloadLength}";

        var node = new ProtocolNode($"TCP, {sp} → {dp}, Len={payloadLength}", at, headerLength);
        node.Add($"순서 번호: {seq}", at + 4, 4);
        node.Add($"확인 번호: {ack}", at + 8, 4);
        node.Add($"플래그: {FlagText(flags)}", at + 13, 1);
        node.Add($"윈도우: {window}", at + 14, 2);
        packet.Layers.Add(node);

        int payloadAt = at + headerLength;
        if (payloadLength == 0 || payloadAt >= f.Length) return;
        ReadOnlySpan<byte> payload = f.AsSpan(payloadAt);
        if (context.IsMcPort(dp) || context.IsMcPort(sp))
        {
            McDissector.TryDissect(payload, payloadAt, packet, toServer: context.IsMcPort(dp));
        }
        else if (dp == context.AdsPort || sp == context.AdsPort)
        {
            AdsDissector.TryDissect(payload, payloadAt, packet, toServer: dp == context.AdsPort);
        }
        else if (McDissector.IsExactFrameRun(payload))
        {
            // MC on a port outside the configured range: accepted only when the length fields
            // account for every byte, and direction comes from the subheader itself.
            McDissector.TryDissect(payload, payloadAt, packet, toServer: payload[0] is 0x50 or 0x54);
        }
    }

    private static void DissectUdp(Packet packet, int at, DissectorContext context)
    {
        byte[] f = packet.Data;
        packet.Protocol = "UDP";
        if (f.Length < at + 8)
        {
            packet.Info = "UDP (잘림)";
            return;
        }

        ushort sp = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at));
        ushort dp = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 2));
        ushort length = BinaryPrimitives.ReadUInt16BigEndian(f.AsSpan(at + 4));
        packet.Udp = new UdpInfo(sp, dp);
        packet.Source = $"{packet.SrcIp}:{sp}";
        packet.Destination = $"{packet.DstIp}:{dp}";
        packet.Info = $"{sp} → {dp} Len={Math.Max(0, length - 8)}";
        packet.Layers.Add(new ProtocolNode($"UDP, {sp} → {dp}", at, 8));

        int payloadAt = at + 8;
        if (payloadAt >= f.Length) return;
        ReadOnlySpan<byte> payload = f.AsSpan(payloadAt);
        if (dp == DissectorContext.GvcpPort || sp == DissectorContext.GvcpPort)
        {
            GigEDissector.TryDissectGvcp(payload, payloadAt, packet, context, fromCamera: sp == DissectorContext.GvcpPort);
        }
        else if (packet.SrcIp is not null && context.IsCamera(packet.SrcIp))
        {
            GigEDissector.TryDissectGvsp(payload, payloadAt, packet);
        }
    }

    private static string FlagText(TcpFlags flags)
    {
        var names = new List<string>(6);
        if (flags.HasFlag(TcpFlags.Syn)) names.Add("SYN");
        if (flags.HasFlag(TcpFlags.Fin)) names.Add("FIN");
        if (flags.HasFlag(TcpFlags.Rst)) names.Add("RST");
        if (flags.HasFlag(TcpFlags.Psh)) names.Add("PSH");
        if (flags.HasFlag(TcpFlags.Ack)) names.Add("ACK");
        if (flags.HasFlag(TcpFlags.Urg)) names.Add("URG");
        return string.Join(", ", names);
    }

    private static string Mac(ReadOnlySpan<byte> bytes)
        => string.Join(":", bytes.ToArray().Select(b => b.ToString("X2")));
}
