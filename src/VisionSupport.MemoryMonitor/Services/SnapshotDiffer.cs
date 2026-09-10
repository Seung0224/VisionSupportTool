using MemMon.Models;

namespace MemMon.Services;

/// <summary>
/// Compares two heap snapshots type by type. This is the leak finder: types whose
/// bytes grew the most between the two snapshots sort to the top.
/// </summary>
public static class SnapshotDiffer
{
    public static IReadOnlyList<DiffRow> Diff(HeapSnapshot before, HeapSnapshot after)
    {
        Dictionary<string, TypeStat> a = before.Types.ToDictionary(t => t.TypeName, StringComparer.Ordinal);
        Dictionary<string, TypeStat> b = after.Types.ToDictionary(t => t.TypeName, StringComparer.Ordinal);

        var rows = new List<DiffRow>(a.Count + b.Count);
        foreach (string name in a.Keys.Union(b.Keys, StringComparer.Ordinal))
        {
            a.TryGetValue(name, out TypeStat? ta);
            b.TryGetValue(name, out TypeStat? tb);
            rows.Add(new DiffRow(name, ta?.Count ?? 0, tb?.Count ?? 0, ta?.TotalBytes ?? 0, tb?.TotalBytes ?? 0));
        }

        rows.Sort((x, y) => y.DeltaBytes.CompareTo(x.DeltaBytes));
        return rows;
    }
}
