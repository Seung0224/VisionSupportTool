namespace VisionSupport.Wireshark.Health;

/// <summary>Card colour: grey, green, yellow, red.</summary>
public enum HealthLevel { Idle, Ok, Warn, Bad }

public enum TargetKind { Mc, Ads, GigE, Nic, Cxp }

public enum AnomalyKind
{
    Timeout, SlowResponse, ErrorResponse, Silence, Retransmit, ConnectionClosed, ZeroWindow,
    FrameDrop, LinkDown, CxpErrors, DeviceRemoved,
}

public sealed record Anomaly(DateTime Time, string TargetId, string TargetName, AnomalyKind Kind,
    HealthLevel Severity, string Text, long? PacketNumber);

public sealed record TargetSnapshot(string Id, TargetKind Kind, string Name, bool Pinned, HealthLevel Level,
    string Summary, long DropCount, double? LastResponseMs, long Bytes);

/// <summary>Spec §5 defaults. Settable so settings.json can tune them per line.</summary>
public sealed class HealthThresholds
{
    public int ResponseTimeoutMs { get; set; } = 1000;
    public int SlowResponseMs { get; set; } = 200;
    public int SlowPerMinute { get; set; } = 5;
    public int SilenceSeconds { get; set; } = 5;
    public int RetransmitsPerMinute { get; set; } = 3;
    /// <summary>How long a target stays red after a one-off problem (RST, timeout, drop).</summary>
    public int RecoverySeconds { get; set; } = 30;
}
