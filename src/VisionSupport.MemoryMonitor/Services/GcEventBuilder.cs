using MemMon.Models;

namespace MemMon.Services;

/// <summary>
/// Folds the raw CLR GC event stream into one row per collection.
///
/// The pause a program actually feels is not the GC itself but the whole stop-the-world
/// window: from when the runtime starts suspending threads to when it has restarted them.
/// That is GCSuspendEEStart to GCRestartEEStop, which is what this pairs up. The runtime
/// also suspends the EE for reasons that are not collections, so a window only counts once
/// a GCStart has been seen inside it.
///
/// Deliberately free of ETW types: this is the part worth unit testing, and doing so must
/// not require an elevated session.
/// </summary>
public sealed class GcEventBuilder
{
    private DateTime? _suspendedAt;
    private int _generation = -1;
    private string _reason = "";
    private string _kind = "";
    private ulong _promotedBytes;
    private long _finalizationPromotedCount;

    public void OnSuspendStart(DateTime at)
    {
        _suspendedAt = at;
        ClearCollectionState();
    }

    public void OnGcStart(int generation, string reason, string kind)
    {
        _generation = generation;
        _reason = reason;
        _kind = kind;
    }

    public void OnHeapStats(ulong promotedBytes, long finalizationPromotedCount)
    {
        _promotedBytes = promotedBytes;
        _finalizationPromotedCount = finalizationPromotedCount;
    }

    /// <summary>Returns the completed collection, or null if this window was not one.</summary>
    public GcEvent? OnRestartStop(DateTime at)
    {
        if (_suspendedAt is null || _generation < 0) return null;

        var completed = new GcEvent(
            at,
            _generation,
            _reason,
            _kind,
            (at - _suspendedAt.Value).TotalMilliseconds,
            _promotedBytes,
            _finalizationPromotedCount);

        _suspendedAt = null;
        ClearCollectionState();
        return completed;
    }

    private void ClearCollectionState()
    {
        _generation = -1;
        _reason = "";
        _kind = "";
        _promotedBytes = 0;
        _finalizationPromotedCount = 0;
    }
}
