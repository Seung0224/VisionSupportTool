using VisionSupport.Wireshark.Dissect;
using Xunit;

namespace VisionSupport.Tests;

public class McDissectorTests
{
    private static Packet Over5000(byte[] payload, bool toPlc = true) => TestFrames.Dissect(toPlc
        ? TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 5000, payload)
        : TestFrames.Tcp("192.168.0.10", 5000, "192.168.0.2", 50000, payload));

    [Fact]
    public void A_3E_batch_read_is_a_request_without_a_serial()
    {
        Packet p = Over5000(TestFrames.McReadRequest3E);

        Assert.Equal("MC", p.Protocol);
        Assert.Equal(new AppMessage(AppKind.Mc, MessageRole.Request, null, "MC 3E 요청 0401 일괄 읽기", false), p.App);
        Assert.Contains(p.Layers, l => l.Text.StartsWith("MELSEC MC 3E"));
    }

    [Fact]
    public void A_3E_response_with_end_code_zero_is_normal()
    {
        Packet p = Over5000(TestFrames.McOkResponse3E, toPlc: false);

        Assert.Equal(MessageRole.Response, p.App!.Role);
        Assert.False(p.App.IsError);
        Assert.Equal("MC 3E 응답 정상", p.Info);
    }

    [Fact]
    public void A_nonzero_end_code_is_an_error()
    {
        Packet p = Over5000(TestFrames.McErrorResponse3E, toPlc: false);

        Assert.True(p.App!.IsError);
        Assert.Equal("MC 3E 응답 이상 종료코드 C059", p.Info);
    }

    [Fact]
    public void A_4E_frame_carries_its_serial_as_the_correlation_id()
    {
        Assert.Equal(0x1234u, Over5000(TestFrames.McReadRequest4E(0x1234)).App!.CorrelationId);
        Assert.Equal(0x1234u, Over5000(TestFrames.McOkResponse4E(0x1234), toPlc: false).App!.CorrelationId);
    }

    [Fact]
    public void Other_bytes_on_an_mc_port_stay_plain_tcp()
    {
        Packet p = Over5000(new byte[] { 0x01, 0x02, 0x03 });

        Assert.Equal("TCP", p.Protocol);
        Assert.Null(p.App);
    }

    [Fact]
    public void Mc_bytes_on_a_port_outside_the_range_are_not_read_as_mc()
        => Assert.Null(TestFrames.Dissect(
            TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 8080, TestFrames.McReadRequest3E)).App);
}
