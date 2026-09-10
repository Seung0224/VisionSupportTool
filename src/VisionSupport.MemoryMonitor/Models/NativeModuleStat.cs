namespace MemMon.Models;

/// <summary>Native memory a single module asked for, and how much of it it gave back.</summary>
public sealed record NativeModuleStat(
    string Module,
    long NetBytes,
    long AllocatedBytes,
    long FreedBytes,
    int AllocCount,
    int FreeCount);
