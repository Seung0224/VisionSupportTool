using MemMon.Models;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace MemMon.Services;

/// <summary>
/// Watches the target's native (VirtualAlloc) memory and attributes it to the module that
/// asked for it.
///
/// This is the only way to see inside growth that never touches the managed heap - a thin
/// managed wrapper over a large native buffer, which is what a VisionPro image is. A heap
/// snapshot cannot see that memory at all, so two snapshots taken at the low and the high
/// point of a multi-gigabyte swing look identical.
///
/// Cost is low: these are bulk allocations, not per-object ones. Measured at roughly four
/// events per second against a target allocating 32 MB/s, so capturing a stack for each one
/// is affordable - unlike the allocation-tick or heap keywords.
/// </summary>
public sealed class NativeAllocMonitor : IDisposable
{
    private const string SessionName = "MemMon-Native";
    private const string Unknown = "(알 수 없음)";

    /// <summary>Bounds on the correlation tables, so a long session cannot grow without limit.</summary>
    private const int MaxPendingStacks = 50_000;
    private const int MaxOutstanding = 300_000;

    private sealed class Counters
    {
        public long Allocated;
        public long Freed;
        public int AllocCount;
        public int FreeCount;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Counters> _byModule = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int Thread, double Ms), ulong> _pendingStacks = new();
    private readonly Dictionary<ulong, (string Module, long Bytes)> _outstanding = new();

    private readonly TraceEventSession _session;
    private readonly Thread _pump;
    private readonly int _pid;

    private NativeModuleMap _modules;
    private DateTime _mapBuiltAt = DateTime.UtcNow;
    private int _allocEvents;
    private int _stacksMatched;
    private bool _disposed;

    public static bool IsElevated => TraceEventSession.IsElevated() == true;

    private NativeAllocMonitor(int pid, TraceEventSession session, Thread pump, NativeModuleMap modules)
    {
        _pid = pid;
        _session = session;
        _pump = pump;
        _modules = modules;
    }

    public static NativeAllocMonitor Start(int pid)
    {
        StopOrphanedSession();

        var session = new TraceEventSession(SessionName) { StopOnDispose = true };
        try
        {
            // ImageLoad keeps the kernel's module view current; the second argument turns on
            // stack capture, which is what makes attribution possible at all.
            session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.VirtualAlloc | KernelTraceEventParser.Keywords.ImageLoad,
                KernelTraceEventParser.Keywords.VirtualAlloc);

            var pump = new Thread(() => session.Source.Process())
            {
                IsBackground = true,
                Name = "MemMon native ETW",
            };
            var monitor = new NativeAllocMonitor(pid, session, pump, NativeModuleMap.ForProcess(pid));
            monitor.Subscribe();
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

    private void Subscribe()
    {
        KernelTraceEventParser kernel = _session.Source.Kernel;

        kernel.VirtualMemAlloc += data =>
        {
            if (data.ProcessID != _pid) return;
            // A pure MEM_RESERVE claims address space without backing it; only committed
            // pages cost real memory, and counting both would double every allocation.
            if ((data.Flags & VirtualAllocTraceData.VirtualAllocFlags.MEM_COMMIT) == 0) return;
            lock (_gate)
            {
                _allocEvents++;
                long bytes = (long)data.Length;
                Count(Unknown).Allocated += bytes;
                Count(Unknown).AllocCount++;

                if (_outstanding.Count < MaxOutstanding)
                    _outstanding[data.BaseAddr] = (Unknown, bytes);
                if (_pendingStacks.Count >= MaxPendingStacks) _pendingStacks.Clear();
                _pendingStacks[(data.ThreadID, data.TimeStampRelativeMSec)] = data.BaseAddr;
            }
        };

        // The stack for an allocation arrives as its own event, pointing back by thread and
        // the timestamp of the event it belongs to.
        kernel.StackWalkStack += data =>
        {
            if (data.ProcessID != _pid) return;
            lock (_gate)
            {
                var key = (data.ThreadID, data.EventTimeStampRelativeMSec);
                if (!_pendingStacks.Remove(key, out ulong address)) return;
                if (!_outstanding.TryGetValue(address, out var entry)) return;
                if (entry.Module != Unknown) return;

                var frames = new ulong[data.FrameCount];
                for (int i = 0; i < data.FrameCount; i++) frames[i] = data.InstructionPointer(i);

                string? owner = _modules.OwnerOf(frames);
                if (owner is null)
                {
                    // A module loaded after we built the map. Rebuild, but not on every miss.
                    if (DateTime.UtcNow - _mapBuiltAt > TimeSpan.FromSeconds(10))
                    {
                        _modules = NativeModuleMap.ForProcess(_pid);
                        _mapBuiltAt = DateTime.UtcNow;
                        owner = _modules.OwnerOf(frames);
                    }
                    if (owner is null) return;
                }

                _stacksMatched++;
                Move(Unknown, owner, entry.Bytes);
                _outstanding[address] = (owner, entry.Bytes);
            }
        };

        kernel.VirtualMemFree += data =>
        {
            if (data.ProcessID != _pid) return;
            lock (_gate)
            {
                if (!_outstanding.TryGetValue(data.BaseAddr, out var entry)) return;

                // A decommit can release part of a region. Credit only what was actually
                // handed back, and keep the entry until the whole region is released -
                // crediting the entire allocation on a partial decommit inflates "freed"
                // by orders of magnitude.
                long freed = Math.Min((long)data.Length, entry.Bytes);
                bool released = (data.Flags & VirtualAllocTraceData.VirtualAllocFlags.MEM_RELEASE) != 0;
                if (released || freed >= entry.Bytes)
                {
                    freed = entry.Bytes;
                    _outstanding.Remove(data.BaseAddr);
                }
                else
                {
                    _outstanding[data.BaseAddr] = (entry.Module, entry.Bytes - freed);
                }

                Counters counters = Count(entry.Module);
                counters.Freed += freed;
                counters.FreeCount++;
            }
        };
    }

    private Counters Count(string module)
    {
        if (!_byModule.TryGetValue(module, out Counters? counters))
        {
            counters = new Counters();
            _byModule[module] = counters;
        }
        return counters;
    }

    /// <summary>Reassigns an allocation once its stack identifies the real owner.</summary>
    private void Move(string from, string to, long bytes)
    {
        Counters source = Count(from);
        source.Allocated -= bytes;
        source.AllocCount--;

        Counters target = Count(to);
        target.Allocated += bytes;
        target.AllocCount++;
    }

    /// <summary>A consistent copy for the UI, largest net growth first.</summary>
    public IReadOnlyList<NativeModuleStat> GetStats()
    {
        lock (_gate)
        {
            return _byModule
                .Where(kv => kv.Value.AllocCount > 0 || kv.Value.FreeCount > 0)
                .Select(kv => new NativeModuleStat(
                    kv.Key,
                    kv.Value.Allocated - kv.Value.Freed,
                    kv.Value.Allocated,
                    kv.Value.Freed,
                    kv.Value.AllocCount,
                    kv.Value.FreeCount))
                .OrderByDescending(s => s.NetBytes)
                .ToList();
        }
    }

    public (long Allocated, long Freed, int Events, int Matched) GetTotals()
    {
        lock (_gate)
        {
            long allocated = _byModule.Values.Sum(c => c.Allocated);
            long freed = _byModule.Values.Sum(c => c.Freed);
            return (allocated, freed, _allocEvents, _stacksMatched);
        }
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
