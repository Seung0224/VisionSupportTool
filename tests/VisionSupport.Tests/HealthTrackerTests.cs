using VisionSupport.Wireshark.Dissect;
using VisionSupport.Wireshark.Health;
using Xunit;

namespace VisionSupport.Tests;

public class HealthTrackerTests
{
    private const string Plc = "192.168.0.10:5000";
    private readonly ManualClock _clock = new();
    private readonly HealthTracker _tracker;
    private long _number;

    public HealthTrackerTests() => _tracker = new HealthTracker(new HealthThresholds(), _clock);

    private Packet Send(byte[] frame)
    {
        Packet p = TestFrames.Dissect(frame, number: ++_number, time: _clock.Now);
        _tracker.Observe(p);
        return p;
    }

    private Packet Request(int clientPort = 50000)
        => Send(TestFrames.Tcp("192.168.0.2", clientPort, "192.168.0.10", 5000, TestFrames.McReadRequest3E));

    private Packet Response(int clientPort = 50000)
        => Send(TestFrames.Tcp("192.168.0.10", 5000, "192.168.0.2", clientPort, TestFrames.McOkResponse3E));

    private TargetSnapshot Plc0() => _tracker.Snapshot().Single(t => t.Id == Plc);

    [Fact]
    public void Mc_traffic_creates_an_unpinned_candidate_named_after_the_plc()
    {
        Request();
        _tracker.Tick();

        TargetSnapshot t = Plc0();
        Assert.Equal(TargetKind.Mc, t.Kind);
        Assert.False(t.Pinned);
        Assert.Equal("PLC(MC) 192.168.0.10:5000", t.Name);
    }

    [Fact]
    public void A_prompt_answer_is_ok_and_shows_the_latency()
    {
        Request();
        _clock.Advance(0.05);
        Response();
        _tracker.Tick();

        Assert.Equal(HealthLevel.Ok, Plc0().Level);
        Assert.Equal("정상 · 응답 50ms", Plc0().Summary);
    }

    [Fact]
    public void No_answer_turns_the_target_bad_and_raises_one_timeout()
    {
        Request();
        _clock.Advance(1.5);
        _tracker.Tick();
        _tracker.Tick();

        Assert.Equal(HealthLevel.Bad, Plc0().Level);
        Assert.Equal("응답 없음 1.5초째", Plc0().Summary);
        Assert.Single(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.Timeout);
    }

    [Fact]
    public void A_late_answer_stays_bad_for_the_recovery_window_then_clears()
    {
        Request();
        _clock.Advance(1.5);
        _tracker.Tick();
        Response();
        _tracker.Tick();
        Assert.Equal(HealthLevel.Bad, Plc0().Level);

        _clock.Advance(31);
        Request();
        Response();
        _tracker.Tick();
        Assert.Equal(HealthLevel.Ok, Plc0().Level);
    }

    [Fact]
    public void Mc_3E_answers_pair_within_their_own_connection()
    {
        Request(50000);
        Request(50001);
        Response(50001);
        _clock.Advance(1.5);
        _tracker.Tick();

        Anomaly timeout = Assert.Single(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.Timeout);
        Assert.Equal(1, timeout.PacketNumber);
    }

    [Fact]
    public void Five_slow_answers_in_a_minute_warn()
    {
        for (int i = 0; i < 5; i++)
        {
            Request();
            _clock.Advance(0.25);
            Response();
        }
        _tracker.Tick();

        Assert.Equal(HealthLevel.Warn, Plc0().Level);
        Assert.Equal("최근 1분 응답 지연 5회", Plc0().Summary);
    }

    [Fact]
    public void An_error_end_code_warns()
    {
        Request();
        Send(TestFrames.Tcp("192.168.0.10", 5000, "192.168.0.2", 50000, TestFrames.McErrorResponse3E));
        _tracker.Tick();

        Assert.Equal(HealthLevel.Warn, Plc0().Level);
        Assert.Contains(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.ErrorResponse);
    }

    /// <summary>Right after start-up nothing has been heard yet. That is "no traffic", not "cut off".</summary>
    [Fact]
    public void A_pinned_target_never_seen_is_idle_not_silent()
    {
        _tracker.Pin(Plc, TargetKind.Mc, "검사기 PLC");
        _clock.Advance(60);
        _tracker.Tick();

        TargetSnapshot t = Plc0();
        Assert.Equal(HealthLevel.Idle, t.Level);
        Assert.Equal("트래픽 없음", t.Summary);
        Assert.Equal("검사기 PLC", t.Name);
        Assert.Empty(_tracker.DrainAnomalies());
        Assert.False(_tracker.HasAlert);
    }

    [Fact]
    public void A_pinned_target_that_goes_quiet_is_bad_once()
    {
        _tracker.Pin(Plc, TargetKind.Mc, "검사기 PLC");
        Request();
        Response();
        _clock.Advance(6);
        _tracker.Tick();
        _tracker.Tick();

        Assert.Equal(HealthLevel.Bad, Plc0().Level);
        Assert.Equal("트래픽 끊김 6초째", Plc0().Summary);
        Assert.Single(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.Silence);
    }

    [Fact]
    public void An_unpinned_candidate_is_never_reported_silent()
    {
        Request();
        Response();
        _clock.Advance(60);
        _tracker.Tick();

        Assert.DoesNotContain(_tracker.DrainAnomalies(), a => a.Kind == AnomalyKind.Silence);
    }

    [Fact]
    public void Only_pinned_targets_raise_the_alert_and_acknowledging_clears_it_until_the_next_problem()
    {
        Request();
        _clock.Advance(1.5);
        _tracker.Tick();
        Assert.False(_tracker.HasAlert);

        _tracker.Pin(Plc, TargetKind.Mc, "검사기 PLC");
        _tracker.Tick();
        Assert.True(_tracker.HasAlert);

        _tracker.Acknowledge();
        Assert.False(_tracker.HasAlert);

        _clock.Advance(1);
        Request();
        _clock.Advance(1.5);
        _tracker.Tick();
        Assert.True(_tracker.HasAlert);
    }

    [Fact]
    public void Unpinning_a_target_never_seen_removes_it()
    {
        _tracker.Pin("10.0.0.1:5000", TargetKind.Mc, "x");
        _tracker.Unpin("10.0.0.1:5000");

        Assert.Empty(_tracker.Snapshot());
    }
}
