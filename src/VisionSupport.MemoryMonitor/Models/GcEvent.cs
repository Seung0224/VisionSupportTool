namespace MemMon.Models;

/// <summary>
/// One garbage collection as observed over ETW: how long it stopped the target, and how much
/// it promoted. Promotion is the leak mechanism - bytes that survive into an older generation
/// are bytes the program is still holding on to.
/// </summary>
public sealed record GcEvent(
    DateTime Timestamp,
    int Generation,
    string Reason,
    string Kind,
    double PauseMs,
    ulong PromotedBytes,
    long FinalizationPromotedCount);
