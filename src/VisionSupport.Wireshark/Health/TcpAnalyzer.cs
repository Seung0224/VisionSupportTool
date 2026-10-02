using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.Health;

[Flags]
internal enum TcpVerdict { None = 0, Retransmission = 1, Reset = 2, Fin = 4, ZeroWindow = 8 }

/// <summary>
/// Per-direction sequence tracking, the way Wireshark's "TCP Retransmission" works at its
/// simplest: data that ends at or before the highest byte already seen was sent before.
/// </summary>
internal sealed class TcpAnalyzer
{
    private const int MaxFlows = 4096;
    private readonly Dictionary<string, uint> _highestEnd = new();

    public TcpVerdict Inspect(Packet p)
    {
        if (p.Tcp is not { } t) return TcpVerdict.None;

        var verdict = TcpVerdict.None;
        if (t.Flags.HasFlag(TcpFlags.Rst)) verdict |= TcpVerdict.Reset;
        if (t.Flags.HasFlag(TcpFlags.Fin)) verdict |= TcpVerdict.Fin;
        if (t.Window == 0 && (t.Flags & (TcpFlags.Rst | TcpFlags.Syn)) == 0) verdict |= TcpVerdict.ZeroWindow;

        string flow = $"{p.Source}>{p.Destination}";
        if (t.Flags.HasFlag(TcpFlags.Syn)) _highestEnd.Remove(flow);
        if (t.PayloadLength == 0) return verdict;

        uint end = unchecked(t.Seq + (uint)t.PayloadLength);
        if (_highestEnd.TryGetValue(flow, out uint highest))
        {
            // A keep-alive re-sends the last byte on purpose.
            bool keepAlive = t.PayloadLength <= 1 && t.Seq == unchecked(highest - 1);
            if (unchecked((int)(end - highest)) > 0) _highestEnd[flow] = end;
            else if (!keepAlive) verdict |= TcpVerdict.Retransmission;
        }
        else
        {
            if (_highestEnd.Count >= MaxFlows) _highestEnd.Clear();
            _highestEnd[flow] = end;
        }
        return verdict;
    }
}
