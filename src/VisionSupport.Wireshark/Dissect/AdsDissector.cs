using System.Buffers.Binary;

namespace VisionSupport.Wireshark.Dissect;

/// <summary>
/// Beckhoff ADS over AMS/TCP (port 48898): a 6-byte AMS/TCP header, then the 32-byte AMS header.
/// The invoke id pairs a response with its request; a device notification answers nothing.
/// </summary>
public static class AdsDissector
{
    private const int AmsHeader = 6;

    public static bool TryDissect(ReadOnlySpan<byte> p, int offset, Packet packet)
    {
        if (p.Length < AmsHeader + 32 || p[0] != 0 || p[1] != 0) return false;

        ReadOnlySpan<byte> h = p[AmsHeader..];
        ushort command = BinaryPrimitives.ReadUInt16LittleEndian(h[16..]);
        ushort stateFlags = BinaryPrimitives.ReadUInt16LittleEndian(h[18..]);
        uint dataLength = BinaryPrimitives.ReadUInt32LittleEndian(h[20..]);
        uint error = BinaryPrimitives.ReadUInt32LittleEndian(h[24..]);
        uint invokeId = BinaryPrimitives.ReadUInt32LittleEndian(h[28..]);
        bool response = (stateFlags & 0x0001) != 0;

        MessageRole role = command == 8 ? MessageRole.Unsolicited
            : response ? MessageRole.Response : MessageRole.Request;
        uint result = response && command != 8 && dataLength >= 4 && h.Length >= 36
            ? BinaryPrimitives.ReadUInt32LittleEndian(h[32..])
            : 0;
        bool isError = error != 0 || result != 0;
        string name = CommandName(command);

        var node = new ProtocolNode($"ADS/AMS, {name} {(role == MessageRole.Request ? "요청" : role == MessageRole.Response ? "응답" : "알림")}",
            offset, Math.Min(p.Length, AmsHeader + 32 + (int)Math.Min(dataLength, int.MaxValue - 64)));
        node.Add($"대상: {NetId(h[..6])}:{BinaryPrimitives.ReadUInt16LittleEndian(h[6..])}", offset + AmsHeader, 8);
        node.Add($"출발: {NetId(h[8..14])}:{BinaryPrimitives.ReadUInt16LittleEndian(h[14..])}", offset + AmsHeader + 8, 8);
        node.Add($"명령: {command} {name}", offset + AmsHeader + 16, 2);
        node.Add($"오류 코드: 0x{error:X}", offset + AmsHeader + 24, 4);
        node.Add($"Invoke ID: {invokeId}", offset + AmsHeader + 28, 4);

        string summary = role switch
        {
            MessageRole.Request => $"ADS {name} 요청 #{invokeId}",
            MessageRole.Response when isError => $"ADS {name} 응답 오류 0x{(error != 0 ? error : result):X}",
            MessageRole.Response => $"ADS {name} 응답 #{invokeId}",
            _ => "ADS 알림(Device Notification)",
        };

        packet.Layers.Add(node);
        packet.Protocol = "ADS";
        packet.Info = summary;
        packet.App = new AppMessage(AppKind.Ads, role, role == MessageRole.Unsolicited ? null : invokeId,
            summary, isError);
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
