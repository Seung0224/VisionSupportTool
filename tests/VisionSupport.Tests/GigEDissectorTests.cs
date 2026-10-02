using System.Net;
using VisionSupport.Wireshark.Dissect;
using Xunit;

namespace VisionSupport.Tests;

public class GigEDissectorTests
{
    private readonly DissectorContext _context = new();

    [Fact]
    public void A_gvcp_command_is_a_request_keyed_by_req_id()
    {
        Packet p = TestFrames.Dissect(TestFrames.GvcpReadRegCmd(42), _context);

        Assert.Equal("GVCP", p.Protocol);
        Assert.Equal(new AppMessage(AppKind.Gvcp, MessageRole.Request, 42u, "GVCP READREG 요청 #42", false), p.App);
    }

    [Fact]
    public void A_gvcp_ack_teaches_the_context_which_host_is_a_camera()
    {
        Packet p = TestFrames.Dissect(TestFrames.GvcpReadRegAck(42), _context);

        Assert.Equal(MessageRole.Response, p.App!.Role);
        Assert.True(_context.IsCamera(IPAddress.Parse(TestFrames.Camera)));
    }

    [Fact]
    public void A_gvcp_ack_with_a_nonzero_status_is_an_error()
        => Assert.True(TestFrames.Dissect(TestFrames.GvcpReadRegAck(1, status: 0x8006), _context).App!.IsError);

    [Fact]
    public void Udp_from_an_unknown_host_is_not_read_as_video()
        => Assert.Null(TestFrames.Dissect(TestFrames.Gvsp(1, 0, GvspFormat.Leader), _context).Gvsp);

    [Fact]
    public void Gvsp_from_a_learned_camera_yields_block_and_packet_ids()
    {
        TestFrames.Dissect(TestFrames.GvcpReadRegAck(1), _context);

        Packet p = TestFrames.Dissect(TestFrames.Gvsp(513, 0x010203, GvspFormat.Payload), _context);

        Assert.Equal("GVSP", p.Protocol);
        Assert.Equal(new GvspHeader(513, 0x010203, GvspFormat.Payload, 0, false), p.Gvsp);
        Assert.Null(p.App);
    }

    [Fact]
    public void Extended_id_gvsp_reads_64_bit_blocks()
    {
        TestFrames.Dissect(TestFrames.GvcpReadRegAck(1), _context);

        Packet p = TestFrames.Dissect(TestFrames.GvspExtended(0x1_0000_0001, 7, GvspFormat.Trailer), _context);

        Assert.Equal(new GvspHeader(0x1_0000_0001, 7, GvspFormat.Trailer, 0, true), p.Gvsp);
    }
}
