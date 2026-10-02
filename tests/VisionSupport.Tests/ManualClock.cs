namespace VisionSupport.Tests;

/// <summary>A clock that only moves when told to. Local time is UTC so packet times built from
/// <see cref="Now"/> and the tracker's own "now" are on the same scale.</summary>
internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(TestFrames.T0);

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public DateTime Now => _now.UtcDateTime;

    public void Advance(double seconds) => _now = _now.AddSeconds(seconds);
}
