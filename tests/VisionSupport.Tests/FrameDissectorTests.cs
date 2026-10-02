using System.Net;
using VisionSupport.Wireshark.Dissect;
using Xunit;

namespace VisionSupport.Tests;

public class FrameDissectorTests
{
    [Fact]
    public void A_tcp_segment_yields_endpoints_flags_and_payload_length()
    {
        byte[] frame = TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 8080, new byte[] { 1, 2, 3 },
            seq: 77, flags: TcpFlags.Ack | TcpFlags.Psh, window: 512);

        Packet p = TestFrames.Dissect(frame);

        Assert.Equal("TCP", p.Protocol);
        Assert.Equal("192.168.0.2:50000", p.Source);
        Assert.Equal("192.168.0.10:8080", p.Destination);
        Assert.Equal(IPAddress.Parse("192.168.0.10"), p.DstIp);
        Assert.Equal(new TcpInfo(50000, 8080, 77, 1, TcpFlags.Ack | TcpFlags.Psh, 512, 3), p.Tcp);
        Assert.Equal(new[] { "Ethernet II", "IPv4", "TCP" }, p.Layers.Select(l => l.Text.Split(',')[0]));
    }

    [Fact]
    public void A_udp_datagram_yields_ports()
    {
        Packet p = TestFrames.Dissect(TestFrames.Udp("10.0.0.1", 1234, "10.0.0.2", 9999, new byte[4]));

        Assert.Equal("UDP", p.Protocol);
        Assert.Equal(new UdpInfo(1234, 9999), p.Udp);
    }

    [Fact]
    public void An_arp_request_reads_as_a_question()
    {
        Packet p = TestFrames.Dissect(TestFrames.Arp("192.168.0.2", "192.168.0.10"));

        Assert.Equal("ARP", p.Protocol);
        Assert.Equal("192.168.0.10 은(는) 누구? 192.168.0.2 에게 알려줘", p.Info);
    }

    /// <summary>pktmon cuts each packet at --pkt-size. The TCP payload length must still be the
    /// real one, from the IP header, or every truncated segment would look like a retransmission.</summary>
    [Fact]
    public void Payload_length_comes_from_the_ip_header_when_the_capture_is_truncated()
    {
        byte[] full = TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 8080, new byte[1000]);
        byte[] cut = full.AsSpan(0, 60).ToArray();

        Packet p = TestFrames.Dissect(cut, originalLength: full.Length);

        Assert.Equal(1000, p.Tcp!.Value.PayloadLength);
        Assert.Equal(full.Length, p.OriginalLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(13)]
    [InlineData(20)]
    [InlineData(40)]
    public void Short_or_garbage_frames_never_throw(int length)
    {
        var junk = new byte[length];
        new Random(length).NextBytes(junk);
        if (length >= 14) { junk[12] = 0x08; junk[13] = 0x00; junk[14 % length] = 0x4F; }

        Packet p = TestFrames.Dissect(junk);

        Assert.NotNull(p.Protocol);
    }
}
