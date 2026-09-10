namespace MemMon.Models;

/// <summary>One type's change between two snapshots.</summary>
public sealed record DiffRow(string TypeName, int CountA, int CountB, ulong BytesA, ulong BytesB)
{
    public int DeltaCount => CountB - CountA;
    public long DeltaBytes => (long)BytesB - (long)BytesA;
}
