using System.Buffers.Binary;

namespace VisionSupport.Wireshark.Dissect;

/// <summary>
/// MELSEC MC protocol, 3E and 4E binary frames. Only the header is read: who asked what, and
/// whether the answer's end code was zero. Device addresses and data stay in the hex view.
/// </summary>
public static class McDissector
{
    public static bool TryDissect(ReadOnlySpan<byte> p, int offset, Packet packet)
    {
        if (p.Length < 2) return false;

        bool is4E, request;
        switch ((p[0], p[1]))
        {
            case (0x50, 0x00): is4E = false; request = true; break;
            case (0xD0, 0x00): is4E = false; request = false; break;
            case (0x54, 0x00): is4E = true; request = true; break;
            case (0xD4, 0x00): is4E = true; request = false; break;
            default: return false;
        }

        // b is where the access route (network no.) starts.
        int b = is4E ? 6 : 2;
        int needed = request ? b + 11 : b + 9;
        if (p.Length < needed) return false;

        string frame = is4E ? "4E" : "3E";
        uint? serial = is4E ? BinaryPrimitives.ReadUInt16LittleEndian(p[2..]) : null;
        var node = new ProtocolNode($"MELSEC MC {frame} 바이너리 {(request ? "요청" : "응답")}", offset, p.Length);
        if (is4E) node.Add($"시리얼 번호: {serial}", offset + 2, 2);
        node.Add($"네트워크 {p[b]} / PC {p[b + 1]} / 국번 {p[b + 4]}", offset + b, 5);
        node.Add($"데이터 길이: {BinaryPrimitives.ReadUInt16LittleEndian(p[(b + 5)..])}", offset + b + 5, 2);

        string summary;
        bool isError;
        if (request)
        {
            ushort command = BinaryPrimitives.ReadUInt16LittleEndian(p[(b + 9)..]);
            string name = CommandName(command);
            node.Add($"감시 타이머: {BinaryPrimitives.ReadUInt16LittleEndian(p[(b + 7)..])}", offset + b + 7, 2);
            node.Add($"커맨드: {command:X4} {name}", offset + b + 9, 2);
            if (p.Length >= b + 13)
            {
                node.Add($"서브커맨드: {BinaryPrimitives.ReadUInt16LittleEndian(p[(b + 11)..]):X4}", offset + b + 11, 2);
            }
            summary = $"MC {frame} 요청 {command:X4} {name}".TrimEnd();
            isError = false;
        }
        else
        {
            ushort endCode = BinaryPrimitives.ReadUInt16LittleEndian(p[(b + 7)..]);
            isError = endCode != 0;
            node.Add($"종료 코드: {endCode:X4} {(isError ? "(이상)" : "(정상)")}", offset + b + 7, 2);
            summary = isError ? $"MC {frame} 응답 이상 종료코드 {endCode:X4}" : $"MC {frame} 응답 정상";
        }

        packet.Layers.Add(node);
        packet.Protocol = "MC";
        packet.Info = summary;
        packet.App = new AppMessage(AppKind.Mc, request ? MessageRole.Request : MessageRole.Response,
            serial, summary, isError);
        return true;
    }

    private static string CommandName(ushort command) => command switch
    {
        0x0401 => "일괄 읽기",
        0x1401 => "일괄 쓰기",
        0x0403 => "랜덤 읽기",
        0x1402 => "랜덤 쓰기",
        0x0406 => "블록 읽기",
        0x1406 => "블록 쓰기",
        0x0619 => "루프백",
        _ => string.Empty,
    };
}
