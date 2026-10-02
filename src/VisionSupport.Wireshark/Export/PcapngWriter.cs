using System.IO;
using System.Text;
using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.Export;

/// <summary>
/// Saves captured packets as pcapng, so anything found here can be opened in the real Wireshark.
/// One section, one Ethernet interface, one Enhanced Packet Block per packet (microsecond times).
/// </summary>
public static class PcapngWriter
{
    public static void Write(Stream stream, IEnumerable<Packet> packets)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        // Section Header Block
        w.Write(0x0A0D0D0Au);
        w.Write(28u);
        w.Write(0x1A2B3C4Du);
        w.Write((ushort)1);
        w.Write((ushort)0);
        w.Write(-1L);
        w.Write(28u);

        // Interface Description Block: LINKTYPE_ETHERNET, no snap length
        w.Write(1u);
        w.Write(20u);
        w.Write((ushort)1);
        w.Write((ushort)0);
        w.Write(0u);
        w.Write(20u);

        foreach (Packet p in packets)
        {
            int padded = (p.Data.Length + 3) & ~3;
            uint total = (uint)(32 + padded);
            ulong micros = (ulong)((p.Time.ToUniversalTime() - DateTime.UnixEpoch).Ticks / 10);

            w.Write(6u);
            w.Write(total);
            w.Write(0u);
            w.Write((uint)(micros >> 32));
            w.Write((uint)micros);
            w.Write((uint)p.Data.Length);
            w.Write((uint)p.OriginalLength);
            w.Write(p.Data);
            w.Write(new byte[padded - p.Data.Length]);
            w.Write(total);
        }
    }
}
