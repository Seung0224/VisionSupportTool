using VisionSupport.Wireshark.Cxp;
using VisionSupport.Wireshark.Dissect;
using VisionSupport.Wireshark.Health;
using Xunit;

namespace VisionSupport.Tests;

public class HealthTrackerLinkTests
{
    private const string Plc = "192.168.0.10:5000";
    private readonly ManualClock _clock = new();
    private readonly HealthTracker _tracker;
    private readonly DissectorContext _context = new();
    private long _number;

    public HealthTrackerLinkTests() => _tracker = new HealthTracker(new HealthThresholds(), _clock);

    private Packet Send(byte[] frame)
    {
        Packet p = TestFrames.Dissect(frame, _context, ++_number, _clock.Now);
        _tracker.Observe(p);
        return p;
    }

    private TargetSnapshot Target(string id) => _tracker.Snapshot().Single(t => t.Id == id);

    // MC request, seq 100..121 (the 3E sample is 21 bytes).
    private Packet McFromVision(uint seq = 100)
        => Send(TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 5000, TestFrames.McReadRequest3E, seq: seq));

    [Fact]
    public void Three_resent_segments_in_a_minute_warn()
    {
        McFromVision();
        Packet last = null!;
        for (int i = 0; i < 3; i++) last = McFromVision();
        _tracker.Tick();

        Assert.True(last.IsAnomalous);
        Assert.Equal(HealthLevel.Warn, Target(Plc).Level);
        Assert.Equal("최근 1분 재전송 3회", Target(Plc).Summary);
    }

    [Fact]
    public void A_keepalive_is_not_a_retransmission()
    {
        McFromVision();
        Packet keepalive = Send(TestFrames.Tcp("192.168.0.2", 50000, "192.168.0.10", 5000, new byte[1], seq: 120));

        Assert.False(keepalive.IsAnomalous);
    }

    [Fact]
    public void A_reset_turns_the_target_bad()
    {
        McFromVision();
        Send(TestFrames.Tcp("192.168.0.10", 5000, "192.168.0.2", 50000, flags: TcpFlags.Rst | TcpFlags.Ack));
        _tracker.Tick();

        Assert.Equal(HealthLevel.Bad, Target(Plc).Level);
        Assert.Equal("연결 리셋(RST)", Target(Plc).Summary);
    }

    [Fact]
    public void A_zero_window_warns()
    {
        McFromVision();
        Send(TestFrames.Tcp("192.168.0.10", 5000, "192.168.0.2", 50000, window: 0));
        _tracker.Tick();

        Assert.Equal(HealthLevel.Warn, Target(Plc).Level);
    }

    private void LearnCamera() => Send(TestFrames.GvcpReadRegAck(1));

    private void Frame(ushort block, params uint[] payloadIds)
    {
        Send(TestFrames.Gvsp(block, 0, GvspFormat.Leader));
        foreach (uint id in payloadIds) Send(TestFrames.Gvsp(block, id, GvspFormat.Payload));
        Send(TestFrames.Gvsp(block, (uint)payloadIds.Length + 1, GvspFormat.Trailer));
    }

    [Fact]
    public void A_skipped_block_id_is_a_dropped_frame()
    {
        LearnCamera();
        Frame(1, 1, 2, 3);
        Frame(3, 1, 2, 3);
        _clock.Advance(2);
        _tracker.Tick();

        TargetSnapshot cam = Target(TestFrames.Camera);
        Assert.Equal(1, cam.DropCount);
        Assert.Equal(HealthLevel.Bad, cam.Level);
        Assert.Equal("프레임 드롭 1장 (누적 1)", cam.Summary);
    }

    [Fact]
    public void A_missing_packet_inside_a_block_is_one_dropped_frame()
    {
        LearnCamera();
        Send(TestFrames.Gvsp(1, 0, GvspFormat.Leader));
        Send(TestFrames.Gvsp(1, 1, GvspFormat.Payload));
        Send(TestFrames.Gvsp(1, 3, GvspFormat.Payload));
        Send(TestFrames.Gvsp(1, 5, GvspFormat.Payload));
        Send(TestFrames.Gvsp(1, 6, GvspFormat.Trailer));
        _clock.Advance(2);
        _tracker.Tick();

        Assert.Equal(1, Target(TestFrames.Camera).DropCount);
    }

    /// <summary>16-bit block ids run 1..65535 and skip 0. Wrapping is not a drop.</summary>
    [Fact]
    public void The_16_bit_block_id_wrapping_past_65535_is_not_a_drop()
    {
        LearnCamera();
        Frame(65534, 1);
        Frame(65535, 1);
        Frame(1, 1);

        Assert.Equal(0, Target(TestFrames.Camera).DropCount);
    }

    /// <summary>"Any GigE camera out there?" goes to a broadcast address; only a camera that answers is a camera.</summary>
    [Theory]
    [InlineData("255.255.255.255")]
    [InlineData("192.168.130.255")]
    public void A_discovery_broadcast_is_not_a_camera(string broadcast)
    {
        var discovery = new byte[8];
        discovery[0] = 0x42;
        discovery[3] = 0x02; // DISCOVERY_CMD
        Send(TestFrames.Udp(TestFrames.Host, 50001, broadcast, 3956, discovery));
        _tracker.Tick();

        Assert.Empty(_tracker.Snapshot());
    }

    [Fact]
    public void A_camera_that_answers_becomes_a_card()
    {
        LearnCamera();
        _tracker.Tick();

        Assert.Equal(TargetKind.GigE, Assert.Single(_tracker.Snapshot()).Kind);
    }

    /// <summary>The NIC card is this PC's own network port - its Windows name may well be "PLC".</summary>
    [Fact]
    public void The_nic_card_says_it_is_a_network_port_and_whether_it_is_connected()
    {
        _tracker.ReportLink("00-11-22-33-44-55", "PLC", up: true);
        _tracker.Tick();

        TargetSnapshot nic = Target("00-11-22-33-44-55");
        Assert.Equal("PLC", nic.Name);
        Assert.Equal("연결됨", nic.Summary);
    }

    /// <summary>On a running line the capture almost always starts in the middle of a frame.</summary>
    [Fact]
    public void Starting_the_capture_mid_frame_is_not_a_drop()
    {
        LearnCamera();
        Send(TestFrames.Gvsp(7, 5, GvspFormat.Payload));
        Send(TestFrames.Gvsp(7, 6, GvspFormat.Trailer));
        Frame(8, 1, 2);
        _clock.Advance(2);
        _tracker.Tick();

        Assert.Equal(0, Target(TestFrames.Camera).DropCount);
    }

    /// <summary>A resent packet of the previous frame, or a stream restarting at block 1, is not 65 534 lost frames.</summary>
    [Fact]
    public void A_block_id_going_back_is_a_resend_or_a_restart_not_a_wrap()
    {
        LearnCamera();
        Frame(1, 1, 2);
        Frame(2, 1, 2);
        Send(TestFrames.Gvsp(1, 2, GvspFormat.Payload)); // resend of block 1
        Frame(3, 1, 2);
        Frame(500, 1);
        Frame(501, 1);
        Frame(1, 1);                                       // stream restarted
        Frame(2, 1);
        _clock.Advance(2);
        _tracker.Tick();

        Assert.Equal(496, Target(TestFrames.Camera).DropCount); // only 4..499 between block 3 and 500
    }

    /// <summary>Frames sent while nobody was capturing were not dropped by the camera.</summary>
    [Fact]
    public void Restarting_the_capture_does_not_count_the_frames_missed_while_stopped()
    {
        LearnCamera();
        Frame(1, 1);
        _tracker.StopListening();
        _tracker.StartListening();
        Frame(5000, 1);
        _clock.Advance(2);
        _tracker.Tick();

        Assert.Equal(0, Target(TestFrames.Camera).DropCount);
    }

    /// <summary>When ETW itself lost events, a gap in the stream may be ours, not the camera's.</summary>
    [Fact]
    public void A_drop_seen_while_the_capture_was_losing_events_is_only_a_warning()
    {
        LearnCamera();
        _tracker.ReportCaptureLoss(10);
        Frame(1, 1);
        Frame(3, 1);
        _tracker.Tick();

        Anomaly drop = Assert.Single(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.FrameDrop);
        Assert.Equal(HealthLevel.Warn, drop.Severity);
        Assert.Equal("프레임 드롭 의심 1장 (캡처 누락 동반)", drop.Text);
        Assert.Equal(HealthLevel.Warn, Target(TestFrames.Camera).Level);
    }

    /// <summary>
    /// The real order on a live line: the gap shows up on the capture thread first, and the ETW
    /// lost-event count is only read on the next one-second tick. That loss still explains the gap.
    /// </summary>
    [Fact]
    public void A_capture_loss_reported_just_after_the_gap_still_makes_it_a_suspicion()
    {
        LearnCamera();
        Frame(1, 1);
        Frame(3, 1);
        _clock.Advance(0.8);
        _tracker.ReportCaptureLoss(10);
        _tracker.Tick();

        Anomaly drop = Assert.Single(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.FrameDrop);
        Assert.Equal(HealthLevel.Warn, drop.Severity);
        Assert.Equal(0, Target(TestFrames.Camera).DropCount);
    }

    [Fact]
    public void A_nic_going_down_is_bad_and_alerts_without_pinning()
    {
        _tracker.ReportLink("00-11-22-33-44-55", "이더넷 2", up: true);
        _tracker.ReportLink("00-11-22-33-44-55", "이더넷 2", up: false);
        _tracker.Tick();

        Assert.Equal(HealthLevel.Bad, Target("00-11-22-33-44-55").Level);
        Assert.True(_tracker.HasAlert);
        Assert.Single(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.LinkDown);
    }

    private static CxpReport Full(long errors, long drops, bool linkUp = true) => new("Rapixo#0", "Rapixo CXP", CxpMode.Full,
        new[] { new CxpConnectionStatus(0, linkUp, "CXP-12", errors, 100, drops) }, null);

    [Fact]
    public void A_missing_board_is_bad()
    {
        _tracker.ReportCxp(new CxpReport("Rapixo#0", "Rapixo CXP", CxpMode.Absent, Array.Empty<CxpConnectionStatus>(), null));
        _tracker.Tick();

        Assert.Equal(HealthLevel.Bad, Target("Rapixo#0").Level);
        Assert.Equal("CXP 보드가 보이지 않음(분리됨)", Target("Rapixo#0").Summary);
    }

    [Fact]
    public void Presence_only_is_grey_with_the_reason()
    {
        _tracker.ReportCxp(new CxpReport("Rapixo#0", "Rapixo CXP", CxpMode.PresenceOnly, Array.Empty<CxpConnectionStatus>(), null));
        _tracker.Tick();

        Assert.Equal(HealthLevel.Idle, Target("Rapixo#0").Level);
        Assert.Equal("상세 불가 — 보드 연결 여부만 감시", Target("Rapixo#0").Summary);
    }

    [Fact]
    public void Rising_cxp_counters_warn_on_errors_and_count_drops()
    {
        _tracker.ReportCxp(Full(errors: 0, drops: 0));
        _tracker.ReportCxp(Full(errors: 2, drops: 0));
        _tracker.Tick();
        Assert.Equal(HealthLevel.Warn, Target("Rapixo#0").Level);

        _tracker.ReportCxp(Full(errors: 2, drops: 3));
        _tracker.Tick();
        Assert.Equal(HealthLevel.Bad, Target("Rapixo#0").Level);
        Assert.Equal(3, Target("Rapixo#0").DropCount);
    }

    [Fact]
    public void A_cxp_link_going_down_is_bad()
    {
        _tracker.ReportCxp(Full(0, 0));
        _tracker.ReportCxp(Full(0, 0, linkUp: false));
        _tracker.Tick();

        Assert.Equal("CXP 링크 끊김 (커넥션 0)", Target("Rapixo#0").Summary);
    }

    /// <summary>The NIC card's chart shows how much went through that port.</summary>
    [Fact]
    public void The_nic_card_carries_the_traffic_counted_on_that_port()
    {
        _tracker.ReportLink("00-11-22-33-44-55", "PLC", up: true);
        _tracker.ReportNicTraffic("00-11-22-33-44-55", 12_345);
        _tracker.Tick();

        Assert.Equal(12_345, Target("00-11-22-33-44-55").Bytes);
    }
}
