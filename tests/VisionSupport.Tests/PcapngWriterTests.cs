using System.IO;
using System.Buffers.Binary;
using VisionSupport.Wireshark.Dissect;
using VisionSupport.Wireshark.Export;
using Xunit;

namespace VisionSupport.Tests;

public class PcapngWriterTests
{
    [Fact]
    public void Writes_a_section_an_ethernet_interface_and_one_block_per_packet()
    {
        var time = new DateTime(2026, 10, 2, 1, 2, 3, DateTimeKind.Utc).AddTicks(4560);
        var packet = new Packet(1, time, new byte[] { 1, 2, 3, 4, 5 }, 60);
        var stream = new MemoryStream();

        PcapngWriter.Write(stream, new[] { packet });
        byte[] b = stream.ToArray();

        // Section Header Block
        Assert.Equal(0x0A0D0D0Au, U32(b, 0));
        Assert.Equal(28u, U32(b, 4));
        Assert.Equal(0x1A2B3C4Du, U32(b, 8));
        Assert.Equal(28u, U32(b, 24));
        // Interface Description Block, Ethernet
        Assert.Equal(1u, U32(b, 28));
        Assert.Equal(20u, U32(b, 32));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(36)));
        // Enhanced Packet Block: 32 header/trailer bytes + 5 data bytes padded to 8
        const int epb = 48;
        Assert.Equal(6u, U32(b, epb));
        Assert.Equal(40u, U32(b, epb + 4));
        ulong micros = (ulong)U32(b, epb + 12) << 32 | U32(b, epb + 16);
        Assert.Equal((ulong)((time - DateTime.UnixEpoch).Ticks / 10), micros);
        Assert.Equal(5u, U32(b, epb + 20));
        Assert.Equal(60u, U32(b, epb + 24));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 0, 0, 0 }, b.AsSpan(epb + 28, 8).ToArray());
        Assert.Equal(40u, U32(b, epb + 36));
        Assert.Equal(epb + 40, b.Length);
    }

    [Fact]
    public void Leaves_the_stream_open()
    {
        var stream = new MemoryStream();
        PcapngWriter.Write(stream, Array.Empty<Packet>());
        Assert.True(stream.CanWrite);
    }

    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
}
