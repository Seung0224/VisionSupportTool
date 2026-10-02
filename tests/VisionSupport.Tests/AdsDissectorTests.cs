using VisionSupport.Wireshark.Dissect;
using Xunit;

namespace VisionSupport.Tests;

public class AdsDissectorTests
{
    private static Packet Ads(byte[] payload, bool toPlc) => TestFrames.Dissect(toPlc
        ? TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.20", 48898, payload)
        : TestFrames.Tcp("192.168.0.20", 48898, "192.168.0.2", 50000, payload));

    [Fact]
    public void A_read_request_is_correlated_by_invoke_id()
    {
        Packet p = Ads(TestFrames.Ads(2, response: false, invokeId: 7), toPlc: true);

        Assert.Equal("ADS", p.Protocol);
        Assert.Equal(new AppMessage(AppKind.Ads, MessageRole.Request, 7u, "ADS Read 요청 #7", false), p.App);
    }

    [Fact]
    public void A_response_with_a_nonzero_result_is_an_error()
    {
        Packet p = Ads(TestFrames.Ads(2, response: true, invokeId: 7, result: 0x710), toPlc: false);

        Assert.Equal(MessageRole.Response, p.App!.Role);
        Assert.True(p.App.IsError);
        Assert.Equal("ADS Read 응답 오류 0x710", p.Info);
    }

    [Fact]
    public void A_device_notification_is_unsolicited()
    {
        Packet p = Ads(TestFrames.Ads(8, response: false, invokeId: 0), toPlc: false);

        Assert.Equal(MessageRole.Unsolicited, p.App!.Role);
        Assert.Null(p.App.CorrelationId);
    }

    [Fact]
    public void A_truncated_ams_header_stays_plain_tcp()
        => Assert.Null(Ads(TestFrames.Ads(2, false, 1).AsSpan(0, 20).ToArray(), toPlc: true).App);
}
