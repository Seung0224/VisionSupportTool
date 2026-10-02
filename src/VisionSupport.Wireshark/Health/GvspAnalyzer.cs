using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.Health;

/// <summary>
/// Frame-drop detection from GVSP headers alone: block ids should step by one, and packet ids
/// inside a block should step by one from the leader's 0. Each broken block counts once.
/// </summary>
internal sealed class GvspAnalyzer
{
    private sealed class Stream
    {
        public ulong? LastBlock;
        public uint NextPacket;
        public bool Broken;
        public bool InBlock;
    }

    private readonly Dictionary<string, Stream> _streams = new();

    /// <returns>Frames this packet shows were lost.</returns>
    public int Inspect(string streamId, GvspHeader h)
    {
        if (!_streams.TryGetValue(streamId, out Stream? s))
        {
            s = new Stream();
            _streams.Add(streamId, s);
        }

        // Tracking starts at the first leader: on a running line the capture nearly always
        // begins in the middle of a frame, and that frame's missing start is ours, not the camera's.
        if (s.LastBlock is null)
        {
            if (h.Format != GvspFormat.Leader) return 0;
            StartBlock(s, h);
            return 0;
        }

        int lost = 0;
        if (s.LastBlock != h.BlockId)
        {
            long step = Step(s.LastBlock.Value, h.BlockId, h.ExtendedId);
            if (step < 0)
            {
                // Backwards: a resent packet of an earlier frame (ignore it), or the stream
                // restarting from block 1 (follow it, nothing lost).
                if (h.Format == GvspFormat.Leader) StartBlock(s, h);
                return 0;
            }

            lost += (int)Math.Min(step - 1, int.MaxValue);
            StartBlock(s, h);
            if (h.Format != GvspFormat.Leader && h.PacketId != 0)
            {
                // This block's leader never arrived.
                s.Broken = true;
                lost += 1;
            }
        }
        else if (s.InBlock)
        {
            if (h.PacketId != s.NextPacket && !s.Broken)
            {
                s.Broken = true;
                lost += 1;
            }
            s.NextPacket = h.PacketId + 1;
        }

        if (h.Format == GvspFormat.Trailer) s.InBlock = false;
        return lost;
    }

    private static void StartBlock(Stream s, GvspHeader h)
    {
        s.LastBlock = h.BlockId;
        s.InBlock = true;
        s.Broken = false;
        s.NextPacket = h.PacketId + 1;
    }

    /// <summary>
    /// How far the block id moved forward; negative when it went back. 16-bit ids run 1..65535
    /// and skip 0, so a fall of more than half the range is a wrap, anything less is going back.
    /// </summary>
    private static long Step(ulong last, ulong current, bool extended)
    {
        if (current > last) return (long)Math.Min(current - last, long.MaxValue);
        if (!extended && last - current >= 32768) return (long)(current + 65535 - last);
        return -1;
    }
}
