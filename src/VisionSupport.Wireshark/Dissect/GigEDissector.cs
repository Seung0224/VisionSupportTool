using System.Buffers.Binary;

namespace VisionSupport.Wireshark.Dissect;

/// <summary>
/// GigE Vision: GVCP (control, UDP 3956, request/ack pairs) and GVSP (the image stream, from the
/// camera to a port the host chose). Only headers are read - GVSP payload is never looked at.
/// </summary>
public static class GigEDissector
{
    public static bool TryDissectGvcp(ReadOnlySpan<byte> p, int offset, Packet packet,
        DissectorContext context, bool fromCamera)
    {
        if (p.Length < 8) return false;

        AppMessage message;
        if (!fromCamera)
        {
            if (p[0] != 0x42) return false;
            ushort command = BinaryPrimitives.ReadUInt16BigEndian(p[2..]);
            ushort reqId = BinaryPrimitives.ReadUInt16BigEndian(p[6..]);
            string summary = $"GVCP {Name(command)} 요청 #{reqId}";
            message = new AppMessage(AppKind.Gvcp, MessageRole.Request, reqId, summary, false);
            var node = new ProtocolNode($"GVCP 명령, {Name(command)}", offset, p.Length);
            node.Add($"명령: 0x{command:X4}", offset + 2, 2);
            node.Add($"Req ID: {reqId}", offset + 6, 2);
            packet.Layers.Add(node);
        }
        else
        {
            ushort status = BinaryPrimitives.ReadUInt16BigEndian(p);
            ushort answer = BinaryPrimitives.ReadUInt16BigEndian(p[2..]);
            ushort ackId = BinaryPrimitives.ReadUInt16BigEndian(p[6..]);
            bool isError = status != 0;
            if (packet.SrcIp is not null) context.LearnCamera(packet.SrcIp);
            MessageRole role = answer == 0x0089 ? MessageRole.Unsolicited : MessageRole.Response;
            string summary = $"GVCP {Name(answer)} 응답 #{ackId}" + (isError ? $" 상태 0x{status:X4}" : string.Empty);
            message = new AppMessage(AppKind.Gvcp, role, role == MessageRole.Response ? ackId : null, summary, isError);
            var node = new ProtocolNode($"GVCP 응답, {Name(answer)}", offset, p.Length);
            node.Add($"상태: 0x{status:X4}", offset, 2);
            node.Add($"Ack ID: {ackId}", offset + 6, 2);
            packet.Layers.Add(node);
        }

        packet.Protocol = "GVCP";
        packet.Info = message.Summary;
        packet.App = message;
        return true;
    }

    public static bool TryDissectGvsp(ReadOnlySpan<byte> p, int offset, Packet packet)
    {
        if (p.Length < 8) return false;

        ushort status = BinaryPrimitives.ReadUInt16BigEndian(p);
        bool extended = (p[4] & 0x80) != 0;
        var format = (GvspFormat)(p[4] & 0x0F);
        ulong block;
        uint packetId;
        if (extended)
        {
            if (p.Length < 20) return false;
            block = BinaryPrimitives.ReadUInt64BigEndian(p[8..]);
            packetId = BinaryPrimitives.ReadUInt32BigEndian(p[16..]);
        }
        else
        {
            block = BinaryPrimitives.ReadUInt16BigEndian(p[2..]);
            packetId = (uint)(p[5] << 16 | p[6] << 8 | p[7]);
        }

        packet.Gvsp = new GvspHeader(block, packetId, format, status, extended);
        packet.Protocol = "GVSP";
        packet.Info = $"GVSP 블록 {block} {FormatName(format)} #{packetId}"
            + (status != 0 ? $" 상태 0x{status:X4}" : string.Empty);
        var node = new ProtocolNode($"GVSP, {FormatName(format)}", offset, extended ? 20 : 8);
        node.Add($"블록 ID: {block}", offset + (extended ? 8 : 2), extended ? 8 : 2);
        node.Add($"패킷 ID: {packetId}", offset + (extended ? 16 : 5), extended ? 4 : 3);
        packet.Layers.Add(node);
        return true;
    }

    private static string FormatName(GvspFormat format) => format switch
    {
        GvspFormat.Leader => "리더",
        GvspFormat.Trailer => "트레일러",
        GvspFormat.Payload => "페이로드",
        GvspFormat.AllIn => "All-in",
        _ => format.ToString(),
    };

    private static string Name(ushort code) => code switch
    {
        0x0089 => "PENDING",
        0x0040 => "PACKETRESEND",
        _ => (code & 0xFFFE) switch
        {
            0x0002 => "DISCOVERY",
            0x0004 => "FORCEIP",
            0x0080 => "READREG",
            0x0082 => "WRITEREG",
            0x0084 => "READMEM",
            0x0086 => "WRITEMEM",
            0x00C0 => "EVENT",
            0x00C2 => "EVENTDATA",
            0x0100 => "ACTION",
            _ => $"0x{code:X4}",
        },
    };
}
