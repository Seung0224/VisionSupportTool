using MemMon.Models;

namespace MemMon.Services;

/// <summary>
/// Decides which collections are worth a person's attention.
///
/// A healthy program produces a steady flood of sub-millisecond gen0/gen1 collections,
/// all AllocSmall and all NonConcurrentGC. That flood is what pushes the one collection
/// that actually stalled the program out of the list. Anything that departs from that
/// baseline - in any of the four ways - is kept separately.
/// </summary>
public static class GcAnomaly
{
    /// <summary>Below this a pause is lost in normal jitter; above it a person would feel it.</summary>
    public const double PauseThresholdMs = 50;

    private const int BaselineMaxGeneration = 1;
    private const string BaselineReason = "AllocSmall";
    private const string BaselineKind = "NonConcurrentGC";

    public static bool IsUnusual(GcEvent gc)
        => gc.PauseMs >= PauseThresholdMs
           || gc.Generation > BaselineMaxGeneration
           || gc.Reason != BaselineReason
           || gc.Kind != BaselineKind;

    /// <summary>Names every way this collection departed from the baseline, for display.</summary>
    public static string Describe(GcEvent gc)
    {
        var reasons = new List<string>(4);
        if (gc.PauseMs >= PauseThresholdMs) reasons.Add($"일시정지 {gc.PauseMs:N0} ms");
        if (gc.Generation > BaselineMaxGeneration) reasons.Add($"세대 {gc.Generation}");
        if (gc.Reason != BaselineReason) reasons.Add($"이유 {gc.Reason}");
        if (gc.Kind != BaselineKind) reasons.Add($"유형 {gc.Kind}");
        return string.Join(" · ", reasons);
    }
}
