using VisionSupport.Wireshark.Cxp;
using VisionSupport.Wireshark.Settings;
using Xunit;

namespace VisionSupport.Tests;

public class CxpMonitorTests
{
    private sealed class FakeSession : IGenTLSession
    {
        private readonly Func<IReadOnlyList<CxpNodeBinding>, IReadOnlyList<NodeReading>> _read;
        public FakeSession(Func<IReadOnlyList<CxpNodeBinding>, IReadOnlyList<NodeReading>> read) => _read = read;
        public bool Disposed { get; private set; }
        public IReadOnlyList<NodeReading> ReadAll(IReadOnlyList<CxpNodeBinding> bindings) => _read(bindings);
        public void Dispose() => Disposed = true;
    }

    private static readonly CxpNodeBinding Link0 = new() { Node = "Link0", Role = CxpNodeRole.LinkUp, Connection = 0 };
    private static readonly CxpNodeBinding Errors0 = new() { Node = "Err0", Role = CxpNodeRole.ErrorCount, Connection = 0 };
    private static readonly CxpNodeBinding Speed0 = new() { Node = "Speed0", Role = CxpNodeRole.Speed, Connection = 0 };
    private static readonly CxpNodeBinding Link1 = new() { Node = "Link1", Role = CxpNodeRole.LinkUp, Connection = 1 };

    private readonly ManualClock _clock = new();
    private readonly List<FakeSession> _opened = new();

    private CxpMonitor Monitor(bool present = true, Func<IReadOnlyList<CxpNodeBinding>, IReadOnlyList<NodeReading>>? read = null,
        bool producer = true, params CxpNodeBinding[] bindings)
    {
        Func<IGenTLSession>? open = producer
            ? () =>
            {
                var s = new FakeSession(read ?? (b => b.Select(x => new NodeReading(x, 1, "1")).ToList()));
                _opened.Add(s);
                return s;
            }
            : null;
        return new CxpMonitor(() => (present, "Matrox Rapixo CXP"), open, bindings, _clock);
    }

    [Fact]
    public void No_board_in_the_device_list_is_absent()
        => Assert.Equal(CxpMode.Absent, Monitor(present: false, bindings: Link0).Poll().Mode);

    [Fact]
    public void Without_a_producer_only_presence_is_watched()
    {
        CxpReport r = Monitor(producer: false, bindings: Link0).Poll();

        Assert.Equal(CxpMode.PresenceOnly, r.Mode);
        Assert.Contains("GenTL 프로듀서를 찾을 수 없음", r.Message);
    }

    [Fact]
    public void Without_bindings_only_presence_is_watched()
        => Assert.Contains("노드가 설정되지 않음", Monitor().Poll().Message);

    [Fact]
    public void Readings_are_grouped_per_connection()
    {
        CxpReport r = Monitor(read: b => new List<NodeReading>
        {
            new(Link0, 1, "True"), new(Errors0, 5, "5"), new(Speed0, 0x58, "CXP12"), new(Link1, 0, "False"),
        }, bindings: new[] { Link0, Errors0, Speed0, Link1 }).Poll();

        Assert.Equal(CxpMode.Full, r.Mode);
        Assert.Equal(new CxpConnectionStatus(0, true, "CXP12", 5, null, null), r.Connections[0]);
        Assert.Equal(new CxpConnectionStatus(1, false, "", null, null, null), r.Connections[1]);
    }

    /// <summary>VISION started after this tool must still be able to open the board.</summary>
    [Fact]
    public void Every_handle_is_released_before_poll_returns()
    {
        CxpMonitor monitor = Monitor(bindings: Link0);
        monitor.Poll();
        monitor.Poll();

        Assert.Equal(2, _opened.Count);
        Assert.All(_opened, s => Assert.True(s.Disposed));
    }

    [Fact]
    public void A_refused_open_degrades_to_presence_and_waits_a_minute_before_retrying()
    {
        int attempts = 0;
        var monitor = new CxpMonitor(() => (true, "Rapixo"),
            () => { attempts++; throw new GenTLException(-1004, "IFOpenDevice"); },
            new[] { Link0 }, _clock);

        CxpReport first = monitor.Poll();
        _clock.Advance(30);
        monitor.Poll();
        _clock.Advance(31);
        monitor.Poll();

        Assert.Equal(CxpMode.PresenceOnly, first.Mode);
        Assert.Contains("다른 프로세스가 사용 중", first.Message);
        Assert.Equal(2, attempts);
    }
}
