namespace MemMon.Models;

/// <summary>A point-in-time aggregation of the target's managed heap, grouped by type.</summary>
public sealed class HeapSnapshot
{
    public required string Label { get; init; }
    public required DateTime TakenAt { get; init; }
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required long TotalObjects { get; init; }
    public required ulong TotalBytes { get; init; }
    /// <summary>Bytes the GC reports as free space between live objects (fragmentation).</summary>
    public required ulong FreeBytes { get; init; }
    public required double WalkSeconds { get; init; }

    /// <summary>
    /// Process-level memory at the moment of the snapshot. Recorded because the type table
    /// only accounts for the managed heap: without these, growth that lives in native memory
    /// simply has nowhere to show up.
    /// </summary>
    public required long PrivateBytes { get; init; }
    public required long WorkingSetBytes { get; init; }
    public required ulong GcCommittedBytes { get; init; }
    public required IReadOnlyList<TypeStat> Types { get; init; }

    /// <summary>
    /// Running native-allocation totals per module at the moment of the snapshot. Comparing two
    /// snapshots gives the native growth in that window, which the type table cannot show.
    /// Empty when native tracking was not running.
    /// </summary>
    public IReadOnlyList<NativeModuleStat> NativeModules { get; init; } = Array.Empty<NativeModuleStat>();

    public string Display => $"{Label}  ·  {ByteSize.Format(TotalBytes)}  ·  {TotalObjects:N0} objs";

    /// <summary>See TargetProcessInfo.ToString: the diff tab's ComboBoxes rely on this too.</summary>
    public override string ToString() => Display;
}
