using MemMon.Models;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;

namespace MemMon.Services;

/// <summary>
/// Listens to the target's CLR GC events over ETW and reports each collection's pause.
///
/// Only <see cref="ClrTraceEventParser.Keywords.GC"/> is enabled - a few events per collection,
/// so the cost to the target is not measurable. The allocation and stack keywords are the
/// expensive ones and are deliberately not used.
///
/// ETW sessions outlive the process that created them, so a crash would leave one running and
/// costing overhead until the machine is rebooted. The fixed session name plus the cleanup in
/// <see cref="Start"/> means the next run reclaims it instead of piling up.
/// </summary>
public sealed class GcEtwMonitor : IDisposable
{
    private const string SessionName = "MemMon-GC";

    private readonly TraceEventSession _session;
    private readonly Thread _pump;
    private bool _disposed;

    /// <summary>ETW real-time sessions can only be created by an elevated process.</summary>
    public static bool IsElevated => TraceEventSession.IsElevated() == true;

    public event EventHandler<GcEvent>? GcObserved;

    private GcEtwMonitor(TraceEventSession session, Thread pump)
    {
        _session = session;
        _pump = pump;
    }

    public static GcEtwMonitor Start(int processId)
    {
        StopOrphanedSession();

        var session = new TraceEventSession(SessionName) { StopOnDispose = true };
        try
        {
            session.EnableProvider(
                ClrTraceEventParser.ProviderGuid,
                TraceEventLevel.Informational,
                (ulong)ClrTraceEventParser.Keywords.GC);

            // Process() blocks until the session stops, so it gets its own thread.
            var pump = new Thread(() => session.Source.Process())
            {
                IsBackground = true,
                Name = "MemMon ETW",
            };
            var monitor = new GcEtwMonitor(session, pump);
            monitor.Subscribe(processId);
            pump.Start();
            return monitor;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private static void StopOrphanedSession()
    {
        if (!TraceEventSession.GetActiveSessionNames().Contains(SessionName)) return;
        new TraceEventSession(SessionName).Stop();
    }

    private void Subscribe(int processId)
    {
        // The session sees every process that has the provider enabled, so each handler has to
        // reject events from anything but the target.
        var builder = new GcEventBuilder();
        ClrTraceEventParser clr = _session.Source.Clr;

        clr.GCSuspendEEStart += data =>
        {
            if (data.ProcessID == processId) builder.OnSuspendStart(data.TimeStamp);
        };

        clr.GCStart += data =>
        {
            if (data.ProcessID == processId)
                builder.OnGcStart(data.Depth, data.Reason.ToString(), data.Type.ToString());
        };

        clr.GCHeapStats += data =>
        {
            if (data.ProcessID != processId) return;
            ulong promoted = (ulong)(data.TotalPromotedSize0 + data.TotalPromotedSize1
                                     + data.TotalPromotedSize2 + data.TotalPromotedSize3);
            builder.OnHeapStats(promoted, data.FinalizationPromotedCount);
        };

        clr.GCRestartEEStop += data =>
        {
            if (data.ProcessID != processId) return;
            GcEvent? collected = builder.OnRestartStop(data.TimeStamp);
            if (collected is not null) GcObserved?.Invoke(this, collected);
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _session.Stop();
        _session.Dispose();
        _pump.Join(TimeSpan.FromSeconds(2));
    }
}
