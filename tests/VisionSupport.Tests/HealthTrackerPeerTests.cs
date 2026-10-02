using System.Net;
using VisionSupport.Wireshark.Dissect;
using VisionSupport.Wireshark.Health;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// A port that carries ordinary traffic still lists who it talks to - every peer, not only PLCs
/// and cameras - so the list is never mysteriously empty.
/// </summary>
public class HealthTrackerPeerTests
{
    private const string Me = "192.168.0.2";
    private readonly ManualClock _clock = new();
    private readonly HealthTracker _tracker;
    private long _number;

    public HealthTrackerPeerTests()
    {
        _tracker = new HealthTracker(new HealthThresholds(), _clock);
        _tracker.SetLocalAddresses(new[] { IPAddress.Parse(Me) });
    }

    private Packet Send(byte[] frame)
    {
        Packet p = TestFrames.Dissect(frame, number: ++_number, time: _clock.Now);
        _tracker.Observe(p);
        return p;
    }

    [Fact]
    public void Ordinary_traffic_lists_the_peer_with_what_it_talks()
    {
        Send(TestFrames.Tcp(Me, 50000, "142.250.1.1", 443, new byte[100]));
        Send(TestFrames.Tcp("142.250.1.1", 443, Me, 50000, new byte[200]));
        _tracker.Tick();

        TargetSnapshot peer = Assert.Single(_tracker.Snapshot());
        Assert.Equal(TargetKind.Host, peer.Kind);
        Assert.Equal("142.250.1.1", peer.Name);
        Assert.Equal("TCP 443", peer.Detail);
        Assert.Equal(HealthLevel.Ok, peer.Level);
    }

    [Theory]
    [InlineData("255.255.255.255")]
    [InlineData("192.168.0.255")]
    [InlineData("239.255.255.250")]
    public void Broadcast_and_multicast_are_not_peers(string to)
    {
        Send(TestFrames.Udp(Me, 50000, to, 1900, new byte[10]));
        _tracker.Tick();

        Assert.Empty(_tracker.Snapshot());
    }

    /// <summary>Web servers reset connections all the time; that is not a fault on an unwatched peer.</summary>
    [Fact]
    public void A_reset_from_an_unpinned_peer_is_not_an_alarm()
    {
        Send(TestFrames.Tcp(Me, 50000, "142.250.1.1", 443, new byte[100]));
        Send(TestFrames.Tcp("142.250.1.1", 443, Me, 50000, flags: TcpFlags.Rst | TcpFlags.Ack));
        _tracker.Tick();

        Assert.Empty(_tracker.DrainAnomalies());
        Assert.Equal(HealthLevel.Ok, _tracker.Snapshot().Single().Level);
    }

    [Fact]
    public void A_pinned_peer_that_resets_is_bad()
    {
        _tracker.Pin("142.250.1.1", TargetKind.Host, "서버");
        Send(TestFrames.Tcp(Me, 50000, "142.250.1.1", 443, new byte[100]));
        Send(TestFrames.Tcp("142.250.1.1", 443, Me, 50000, flags: TcpFlags.Rst | TcpFlags.Ack));
        _tracker.Tick();

        Assert.Equal(HealthLevel.Bad, _tracker.Snapshot().Single().Level);
    }

    [Fact]
    public void A_plc_is_one_row_not_also_a_peer()
    {
        Send(TestFrames.Tcp(Me, 50000, "192.168.0.10", 5000, TestFrames.McReadRequest3E));
        _tracker.Tick();

        Assert.Equal(TargetKind.Mc, Assert.Single(_tracker.Snapshot()).Kind);
    }

    [Fact]
    public void Busier_peers_list_first()
    {
        Send(TestFrames.Tcp(Me, 50000, "10.0.0.1", 80, new byte[10]));
        Send(TestFrames.Tcp(Me, 50001, "10.0.0.2", 80, new byte[1000]));
        _tracker.Tick();

        Assert.Equal(new[] { "10.0.0.2", "10.0.0.1" }, _tracker.Snapshot().Select(t => t.Id));
    }

    [Fact]
    public void The_peer_list_is_bounded()
    {
        for (int i = 0; i < 400; i++)
        {
            Send(TestFrames.Tcp(Me, 50000, $"10.1.{i / 250}.{i % 250 + 1}", 80, new byte[10]));
            _clock.Advance(0.01);
        }
        _tracker.Tick();

        Assert.Equal(HealthTracker.MaxPeers, _tracker.Snapshot().Count);
    }

    [Fact]
    public void Without_knowing_our_own_address_no_peers_are_guessed()
    {
        var tracker = new HealthTracker(new HealthThresholds(), _clock);
        tracker.Observe(TestFrames.Dissect(TestFrames.Tcp(Me, 50000, "142.250.1.1", 443, new byte[100])));

        Assert.Empty(tracker.Snapshot());
    }
}
