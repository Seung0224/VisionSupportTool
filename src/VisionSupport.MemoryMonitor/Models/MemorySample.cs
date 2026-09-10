namespace MemMon.Models;

/// <summary>One periodic reading of the target's managed heap and process memory.</summary>
public sealed record MemorySample(
    double ElapsedSeconds,
    DateTime Timestamp,
    ulong Gen0Bytes,
    ulong Gen1Bytes,
    ulong Gen2Bytes,
    ulong LohBytes,
    ulong PohBytes,
    long PrivateBytes,
    long WorkingSetBytes)
{
    public ulong ManagedTotalBytes => Gen0Bytes + Gen1Bytes + Gen2Bytes + LohBytes + PohBytes;
}
