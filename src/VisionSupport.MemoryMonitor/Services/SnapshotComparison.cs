using MemMon.Models;

namespace MemMon.Services;

/// <summary>How a process's memory changed between two snapshots, split by where it lives.</summary>
public sealed record MemoryDelta(long PrivateBytes, long ManagedBytes, long NativeBytes)
{
    /// <summary>Share of the growth that happened outside the managed heap. 0 when nothing grew.</summary>
    public double NativeShare => PrivateBytes <= 0 || NativeBytes <= 0
        ? 0
        : (double)NativeBytes / PrivateBytes;
}

/// <summary>
/// Attributes the growth between two snapshots to the managed heap or to everything else.
///
/// The type table can only ever account for the managed heap. A program that grows by
/// gigabytes of native memory - a thin managed wrapper over a large native buffer, which is
/// what a VisionPro image is - produces two nearly identical type tables and no explanation.
/// This puts the missing number on screen instead of leaving it invisible.
/// </summary>
public static class SnapshotComparison
{
    public static MemoryDelta Compare(HeapSnapshot before, HeapSnapshot after)
    {
        long privateDelta = after.PrivateBytes - before.PrivateBytes;
        long managedDelta = (long)after.GcCommittedBytes - (long)before.GcCommittedBytes;
        return new MemoryDelta(privateDelta, managedDelta, privateDelta - managedDelta);
    }
}
