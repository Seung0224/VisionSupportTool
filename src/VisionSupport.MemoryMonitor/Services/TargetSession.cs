using System.Diagnostics;
using MemMon.Models;
using Microsoft.Diagnostics.Runtime;

namespace MemMon.Services;

/// <summary>
/// Owns the connection to one monitored process.
///
/// Two kinds of read happen here and they need different attaches. Periodic sampling
/// must never stop the target, so it reuses one long-lived suspend:false attach and only
/// reads segment sizes. A full heap walk, by contrast, produces garbage if the heap moves
/// underneath it, so it takes a fresh suspend:true attach for the duration of the walk.
/// A single gate serialises the two, so sampling pauses while a snapshot runs.
/// </summary>
public sealed class TargetSession : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DataTarget _dataTarget;
    private readonly ClrRuntime _runtime;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _disposed;

    public Process Process { get; }
    public string ClrDescription { get; }
    public int Pid { get; }

    private TargetSession(Process process, DataTarget dataTarget, ClrRuntime runtime, string clrDescription)
    {
        Process = process;
        Pid = process.Id;
        _dataTarget = dataTarget;
        _runtime = runtime;
        ClrDescription = clrDescription;
    }

    public static TargetSession Attach(int pid)
    {
        Process process = Process.GetProcessById(pid);
        DataTarget dataTarget = DataTarget.AttachToProcess(pid, suspend: false);
        try
        {
            if (dataTarget.ClrVersions.Length == 0)
            {
                throw new InvalidOperationException(
                    "이 프로세스에서 CLR을 찾지 못했습니다. 네이티브 프로세스이거나, " +
                    "대상이 32비트라 64비트 MemMon이 읽을 수 없는 경우입니다.");
            }

            ClrInfo info = dataTarget.ClrVersions[0];
            ClrRuntime runtime = info.CreateRuntime();
            // ClrFlavor.Desktop is ClrMD's name for .NET Framework; show the name people use.
            string family = info.Flavor == ClrFlavor.Desktop ? ".NET Framework" : ".NET";
            return new TargetSession(process, dataTarget, runtime, $"{family} {info.Version}");
        }
        catch
        {
            dataTarget.Dispose();
            process.Dispose();
            throw;
        }
    }

    /// <summary>Reads per-generation heap sizes without stopping the target (~1-3 ms).</summary>
    public async Task<MemorySample> SampleAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(SampleCore, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private MemorySample SampleCore()
    {
        _runtime.FlushCachedData();
        ClrHeap heap = _runtime.Heap;

        ulong gen0 = 0, gen1 = 0, gen2 = 0, loh = 0, poh = 0;
        foreach (ClrSegment segment in heap.Segments)
        {
            switch (segment.Kind)
            {
                case GCSegmentKind.Large:
                    loh += segment.ObjectRange.Length;
                    break;
                case GCSegmentKind.Pinned:
                    poh += segment.ObjectRange.Length;
                    break;
                default:
                    gen0 += segment.Generation0.Length;
                    gen1 += segment.Generation1.Length;
                    gen2 += segment.Generation2.Length;
                    break;
            }
        }

        Process.Refresh();
        return new MemorySample(
            _clock.Elapsed.TotalSeconds,
            DateTime.Now,
            gen0, gen1, gen2, loh, poh,
            Process.PrivateMemorySize64,
            Process.WorkingSet64);
    }

    /// <summary>Suspends the target, walks every live object, and aggregates by type.</summary>
    public async Task<HeapSnapshot> TakeSnapshotAsync(
        string label, IReadOnlyList<NativeModuleStat> nativeModules, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => SnapshotCore(label, nativeModules, ct), ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private HeapSnapshot SnapshotCore(
        string label, IReadOnlyList<NativeModuleStat> nativeModules, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        using DataTarget frozen = DataTarget.AttachToProcess(Pid, suspend: true);
        using ClrRuntime runtime = frozen.ClrVersions[0].CreateRuntime();

        var stats = new Dictionary<string, (int Count, ulong Bytes)>(StringComparer.Ordinal);
        long objects = 0;
        ulong totalBytes = 0;
        ulong freeBytes = 0;
        int sinceCancelCheck = 0;

        foreach (ClrObject obj in runtime.Heap.EnumerateObjects())
        {
            if (++sinceCancelCheck >= 65536)
            {
                sinceCancelCheck = 0;
                ct.ThrowIfCancellationRequested();
            }

            ClrType? type = obj.Type;
            if (type is null) continue;

            ulong size = obj.Size;
            if (type.IsFree)
            {
                freeBytes += size;
                continue;
            }

            objects++;
            totalBytes += size;

            string name = type.Name ?? "<unknown>";
            stats.TryGetValue(name, out (int Count, ulong Bytes) current);
            stats[name] = (current.Count + 1, current.Bytes + size);
        }

        stopwatch.Stop();
        List<TypeStat> types = stats
            .Select(kv => new TypeStat(kv.Key, kv.Value.Count, kv.Value.Bytes))
            .OrderByDescending(t => t.TotalBytes)
            .ToList();

        // Record where the rest of the process's memory is, so growth that never touches the
        // managed heap still has somewhere to show up when two snapshots are compared.
        ulong committed = 0;
        foreach (ClrSegment segment in runtime.Heap.Segments) committed += segment.CommittedMemory.Length;
        Process.Refresh();

        return new HeapSnapshot
        {
            Label = label,
            TakenAt = DateTime.Now,
            ProcessId = Pid,
            ProcessName = Process.ProcessName,
            TotalObjects = objects,
            TotalBytes = totalBytes,
            FreeBytes = freeBytes,
            WalkSeconds = stopwatch.Elapsed.TotalSeconds,
            PrivateBytes = Process.PrivateMemorySize64,
            WorkingSetBytes = Process.WorkingSet64,
            GcCommittedBytes = committed,
            Types = types,
            NativeModules = nativeModules,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runtime.Dispose();
        _dataTarget.Dispose();
        Process.Dispose();
        _gate.Dispose();
    }
}
