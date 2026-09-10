namespace MemMon.Models;

/// <summary>How much of the heap one managed type accounts for in a snapshot.</summary>
public sealed record TypeStat(string TypeName, int Count, ulong TotalBytes)
{
    public double AverageBytes => Count == 0 ? 0 : (double)TotalBytes / Count;
}
