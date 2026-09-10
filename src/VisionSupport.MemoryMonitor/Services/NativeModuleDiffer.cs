using MemMon.Models;

namespace MemMon.Services;

/// <summary>
/// Compares the native allocation counters carried by two snapshots.
///
/// Snapshots hold running totals, so the difference between them is what the target did in
/// that window - the same question the managed type diff answers, for the memory the managed
/// heap cannot see.
/// </summary>
public static class NativeModuleDiffer
{
    public static IReadOnlyList<NativeModuleStat> Diff(HeapSnapshot before, HeapSnapshot after)
    {
        Dictionary<string, NativeModuleStat> a =
            before.NativeModules.ToDictionary(m => m.Module, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, NativeModuleStat> b =
            after.NativeModules.ToDictionary(m => m.Module, StringComparer.OrdinalIgnoreCase);

        var rows = new List<NativeModuleStat>();
        foreach (string module in a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase))
        {
            a.TryGetValue(module, out NativeModuleStat? start);
            b.TryGetValue(module, out NativeModuleStat? end);

            long allocated = (end?.AllocatedBytes ?? 0) - (start?.AllocatedBytes ?? 0);
            long freed = (end?.FreedBytes ?? 0) - (start?.FreedBytes ?? 0);
            int allocCount = (end?.AllocCount ?? 0) - (start?.AllocCount ?? 0);
            int freeCount = (end?.FreeCount ?? 0) - (start?.FreeCount ?? 0);

            // Nothing happened to this module in the window; it would only be noise.
            if (allocated == 0 && freed == 0 && allocCount == 0 && freeCount == 0) continue;

            rows.Add(new NativeModuleStat(
                module, allocated - freed, allocated, freed, allocCount, freeCount));
        }

        rows.Sort((x, y) => y.NetBytes.CompareTo(x.NetBytes));
        return rows;
    }
}
