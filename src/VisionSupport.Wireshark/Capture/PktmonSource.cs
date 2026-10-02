using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace VisionSupport.Wireshark.Capture;

/// <summary>
/// Live packets from Windows' built-in packet monitor, with nothing to install: `pktmon start`
/// makes the driver emit one ETW event per packet, and a real-time session of ours receives them.
///
/// Both halves outlive this process if it dies - the ETW session and the pktmon capture - so
/// <see cref="Start"/> first clears up after a previous run that never reached <see cref="Stop"/>.
/// </summary>
public sealed class PktmonSource : IDisposable
{
    public const string SessionName = "VisionSupport-Wireshark";

    private static readonly Guid PktMonProvider = new("4d4f80d9-c8bd-4d73-bb5b-19c90402c5ac");
    private const int EthernetPacketType = 1;

    private readonly Action<RawFrame> _onFrame;
    private readonly RecentPacketIds _seen = new(8192);
    private TraceEventSession? _session;
    private Thread? _pump;
    private bool _pktmonStarted;

    public PktmonSource(Action<RawFrame> onFrame) => _onFrame = onFrame;

    /// <summary>The ETW pump died. Raised on the pump thread.</summary>
    public event Action<string>? Faulted;

    /// <summary>Events ETW dropped because we could not keep up. Cumulative.</summary>
    public long EventsLost => _session?.EventsLost ?? 0;

    public void Start(int packetSize)
    {
        if (_session is not null) return;

        // A leftover session means a previous run of ours crashed mid-capture, so the pktmon
        // capture it started is ours to stop too.
        if (StopLeftoverSession()) Pktmon.Run("stop");

        Pktmon.Run("filter remove");
        (int code, string output) = Pktmon.Run($"start --capture --comp nics --pkt-size {packetSize} -m memory -s 16");
        if (code != 0)
        {
            string reason = output.Trim();
            throw new InvalidOperationException(reason.Length > 0
                ? reason + " (다른 pktmon 캡처가 돌고 있다면 'pktmon stop' 후 다시 시도)"
                : $"pktmon 종료 코드 {code}");
        }
        _pktmonStarted = true;

        try
        {
            var session = new TraceEventSession(SessionName) { StopOnDispose = true };
            session.EnableProvider(PktMonProvider, TraceEventLevel.Verbose, ulong.MaxValue);
            session.Source.Dynamic.All += OnEvent;
            _session = session;
            _pump = new Thread(Pump) { IsBackground = true, Name = "pktmon ETW" };
            _pump.Start();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        TraceEventSession? session = _session;
        _session = null;
        session?.Dispose();
        _pump?.Join(2000);
        _pump = null;

        if (!_pktmonStarted) return;
        _pktmonStarted = false;
        try
        {
            Pktmon.Run("stop");
        }
        catch (InvalidOperationException)
        {
            // Nothing more to do from here; the next Start cleans up.
        }
    }

    public void Dispose() => Stop();

    private void Pump()
    {
        try
        {
            _session?.Source.Process();
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex.Message);
        }
    }

    private void OnEvent(TraceEvent e)
    {
        if (e.PayloadIndex("Payload") < 0) return;
        if (Convert.ToInt32(e.PayloadByName("PacketType")) != EthernetPacketType) return;
        if (!_seen.Add(Convert.ToUInt64(e.PayloadByName("PktGroupId")), Convert.ToUInt32(e.PayloadByName("PktNumber")))) return;
        if (e.PayloadByName("Payload") is not byte[] { Length: > 0 } data) return;

        _onFrame(new RawFrame(e.TimeStamp, data,
            Convert.ToInt32(e.PayloadByName("OriginalPayloadSize")),
            Convert.ToInt32(e.PayloadByName("ComponentId"))));
    }

    private static bool StopLeftoverSession()
    {
        if (!TraceEventSession.GetActiveSessionNames().Contains(SessionName)) return false;
        using var leftover = new TraceEventSession(SessionName, TraceEventSessionOptions.Attach);
        leftover.Stop();
        return true;
    }
}
