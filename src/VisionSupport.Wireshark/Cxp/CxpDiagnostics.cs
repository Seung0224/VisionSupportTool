using System.Text;
using VisionSupport.Wireshark.Settings;

namespace VisionSupport.Wireshark.Cxp;

/// <summary>
/// Everything needed to decide which nodes to bind, as text: is the board there, can the
/// interface and camera be opened right now (run it while VISION is grabbing), and every
/// link/error/frame-looking node with its current value.
/// </summary>
public static class CxpDiagnostics
{
    private static readonly string[] Interesting =
        { "Link", "Cxp", "Error", "Crc", "Frame", "Drop", "Lost", "Speed", "Connection", "Packet", "Status" };

    public static string Run(WiresharkSettings settings)
    {
        var o = new StringBuilder();
        o.AppendLine($"통신 모니터 CXP 진단 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        (bool present, string name) = RapixoPresence.Query(settings.CxpDeviceNameMatch);
        o.AppendLine($"장치 관리자 '{settings.CxpDeviceNameMatch}': {(present ? name + " (정상)" : "없음 또는 오류")}");
        string? producer = GenTL.FindProducer(settings.GenTLProducerPath);
        o.AppendLine($"GenTL 프로듀서: {producer ?? "없음"}");
        if (producer is null) return o.ToString();

        try
        {
            using var gentl = new GenTL(producer);
            IntPtr tl = gentl.OpenSystem();
            try
            {
                foreach (string ifaceId in gentl.InterfaceIds(tl)) DumpInterface(o, gentl, tl, ifaceId);
            }
            finally
            {
                gentl.CloseSystem(tl);
            }
        }
        catch (Exception ex)
        {
            o.AppendLine($"GenTL 오류: {ex.Message}");
        }
        return o.ToString();
    }

    private static void DumpInterface(StringBuilder o, GenTL gentl, IntPtr tl, string ifaceId)
    {
        o.AppendLine($"인터페이스 {ifaceId}");
        IntPtr iface = IntPtr.Zero;
        try
        {
            iface = gentl.OpenInterface(tl, ifaceId);
            o.AppendLine("  열기: 성공");
            DumpNodeMap(o, "  [Interface]", gentl, iface);
            foreach (string deviceId in gentl.DeviceIds(iface))
            {
                o.AppendLine($"  장치 {deviceId}, 접근 상태 {Try(() => gentl.AccessStatus(iface, deviceId).ToString())}");
                IntPtr device = IntPtr.Zero;
                try
                {
                    device = gentl.OpenDeviceReadOnly(iface, deviceId);
                    o.AppendLine("    읽기 전용 열기: 성공");
                    DumpNodeMap(o, "    [Device]", gentl, gentl.DevicePort(device));
                }
                catch (Exception ex)
                {
                    o.AppendLine($"    읽기 전용 열기: 실패 — {ex.Message}");
                }
                finally
                {
                    if (device != IntPtr.Zero) gentl.CloseDevice(device);
                }
            }
        }
        catch (Exception ex)
        {
            o.AppendLine($"  실패 — {ex.Message}");
        }
        finally
        {
            if (iface != IntPtr.Zero) gentl.CloseInterface(iface);
        }
    }

    private static void DumpNodeMap(StringBuilder o, string prefix, GenTL gentl, IntPtr port)
    {
        try
        {
            string url = gentl.PortUrls(port).FirstOrDefault() ?? throw new InvalidOperationException("XML 위치 없음");
            o.AppendLine($"{prefix} XML {url}");
            GenApiNodeMap map = GenApiNodeMap.Parse(GenApiXml.Load(url, (a, l) => gentl.Read(port, a, l)));
            foreach (string node in map.NodeNames
                .Where(n => Interesting.Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(n => n, StringComparer.Ordinal))
            {
                o.AppendLine($"{prefix}   {node} = {Try(() => map.Read(node, (a, l) => gentl.Read(port, a, l)).Text)}");
            }
        }
        catch (Exception ex)
        {
            o.AppendLine($"{prefix} 노드맵 실패 — {ex.Message}");
        }
    }

    private static string Try(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            return "읽기 불가: " + ex.Message;
        }
    }
}
