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

        int lost = 0;
        if (s.LastBlock != h.BlockId)
        {
            if (s.LastBlock is { } last) lost += Gap(last, h.BlockId, h.ExtendedId);
            s.LastBlock = h.BlockId;
            s.InBlock = true;
            s.Broken = false;
            if (h.Format != GvspFormat.Leader && h.PacketId != 0)
            {
                // This block's leader never arrived.
                s.Broken = true;
                lost += 1;
            }
            s.NextPacket = h.PacketId + 1;
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

    /// <summary>Whole frames skipped between two block ids. 16-bit ids wrap 65535 → 1 (0 is never used).</summary>
    private static int Gap(ulong last, ulong current, bool extended)
    {
        ulong step = extended
            ? (current > last ? current - last : 1)
            : (current > last ? current - last : current + 65535 - last);
        return step > 1 ? (int)Math.Min(step - 1, int.MaxValue) : 0;
    }
}
