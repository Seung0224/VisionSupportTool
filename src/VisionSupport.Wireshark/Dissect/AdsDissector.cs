using System.Buffers.Binary;

namespace VisionSupport.Wireshark.Dissect;

/// <summary>
/// Beckhoff ADS over AMS/TCP (port 48898): a 6-byte AMS/TCP header, then the 32-byte AMS header.
/// The invoke id pairs a response with its request; a device notification answers nothing.
///
/// A segment is only read as AMS when the header holds together - lengths agree, the command is
/// a real one, and it travels the right way. A large Read answer spans several segments, and a
/// zero-filled continuation would otherwise look like a request with invoke id 0. A PLC may also
/// answer several requests in one segment, so every message in it is read.
/// </summary>
public static class AdsDissector
{
    private const int AmsTcpHeader = 6;
    private const int AmsHeader = 32;

    public static bool TryDissect(ReadOnlySpan<byte> p, int offset, Packet packet, bool toServer)
    {
        int at = 0;
        while (TryReadOne(p[at..], offset + at, packet, toServer, out int length))
        {
            at += length;
            if (at >= p.Length) break;
        }
        if (packet.Messages.Count == 0) return false;

        packet.Protocol = "ADS";
        packet.Info = packet.Messages.Count == 1
            ? packet.Messages[0].Summary
            : $"{packet.Messages[0].Summary} 외 {packet.Messages.Count - 1}건";
        return true;
    }

    private static bool TryReadOne(ReadOnlySpan<byte> p, int offset, Packet packet, bool toServer, out int length)
    {
        length = 0;
        if (p.Length < AmsTcpHeader + AmsHeader || p[0] != 0 || p[1] != 0) return false;

        uint amsLength = BinaryPrimitives.ReadUInt32LittleEndian(p[2..]);
        ReadOnlySpan<byte> h = p[AmsTcpHeader..];
        ushort command = BinaryPrimitives.ReadUInt16LittleEndian(h[16..]);
        ushort stateFlags = BinaryPrimitives.ReadUInt16LittleEndian(h[18..]);
        uint dataLength = BinaryPrimitives.ReadUInt32LittleEndian(h[20..]);
        uint error = BinaryPrimitives.ReadUInt32LittleEndian(h[24..]);
        uint invokeId = BinaryPrimitives.ReadUInt32LittleEndian(h[28..]);
        bool response = (stateFlags & 0x0001) != 0;

        if (command is < 1 or > 9 || amsLength != AmsHeader + (ulong)dataLength) return false;

        MessageRole role = command == 8 ? MessageRole.Unsolicited
            : response ? MessageRole.Response : MessageRole.Request;
        // Requests go to the PLC's port; answers and notifications come from it.
        if ((role == MessageRole.Request) != toServer) return false;

        uint result = role == MessageRole.Response && dataLength >= 4 && h.Length >= AmsHeader + 4
            ? BinaryPrimitives.ReadUInt32LittleEndian(h[AmsHeader..])
            : 0;
        bool isError = error != 0 || result != 0;
        string name = CommandName(command);

        length = (int)Math.Min(AmsTcpHeader + (long)amsLength, int.MaxValue);
        var node = new ProtocolNode(
            $"ADS/AMS, {name} {(role == MessageRole.Request ? "요청" : role == MessageRole.Response ? "응답" : "알림")}",
            offset, Math.Min(p.Length, length));
        node.Add($"대상: {NetId(h[..6])}:{BinaryPrimitives.ReadUInt16LittleEndian(h[6..])}", offset + AmsTcpHeader, 8);
        node.Add($"출발: {NetId(h[8..14])}:{BinaryPrimitives.ReadUInt16LittleEndian(h[14..])}", offset + AmsTcpHeader + 8, 8);
        node.Add($"명령: {command} {name}", offset + AmsTcpHeader + 16, 2);
        node.Add($"오류 코드: 0x{error:X}", offset + AmsTcpHeader + 24, 4);
        node.Add($"Invoke ID: {invokeId}", offset + AmsTcpHeader + 28, 4);
        packet.Layers.Add(node);

        string summary = role switch
        {
            MessageRole.Request => $"ADS {name} 요청 #{invokeId}",
            MessageRole.Response when isError => $"ADS {name} 응답 오류 0x{(error != 0 ? error : result):X}",
            MessageRole.Response => $"ADS {name} 응답 #{invokeId}",
            _ => "ADS 알림(Device Notification)",
        };
        packet.AddMessage(new AppMessage(AppKind.Ads, role, role == MessageRole.Unsolicited ? null : invokeId,
            summary, isError));
        return true;
    }

    private static string NetId(ReadOnlySpan<byte> b) => $"{b[0]}.{b[1]}.{b[2]}.{b[3]}.{b[4]}.{b[5]}";

    private static string CommandName(ushort command) => command switch
    {
        1 => "ReadDeviceInfo",
        2 => "Read",
        3 => "Write",
        4 => "ReadState",
        5 => "WriteControl",
        6 => "AddNotification",
        7 => "DeleteNotification",
        8 => "Notification",
        9 => "ReadWrite",
        _ => $"명령{command}",
    };
}
