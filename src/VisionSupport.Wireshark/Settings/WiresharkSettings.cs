using VisionSupport.Wireshark.Health;

namespace VisionSupport.Wireshark.Settings;

public sealed class PinnedTarget
{
    public string Id { get; set; } = string.Empty;
    public TargetKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
}

public enum CxpNodeRole { LinkUp, Speed, ErrorCount, FrameCount, DropCount }

/// <summary>One GenICam feature to read each poll, and what it means. Which names exist depends on
/// the board and driver, so they come from the diagnostics dump (Task 17), not from code.</summary>
public sealed class CxpNodeBinding
{
    /// <summary>"Interface" (the grabber's node map) or "Device" (the camera's).</summary>
    public string Module { get; set; } = "Interface";
    public string Node { get; set; } = string.Empty;
    public CxpNodeRole Role { get; set; }
    public int Connection { get; set; }
}

public sealed class WiresharkSettings
{
    public List<PinnedTarget> Pinned { get; set; } = new();
    public HealthThresholds Thresholds { get; set; } = new();
    public int McPortMin { get; set; } = 5000;
    public int McPortMax { get; set; } = 5010;
    /// <summary>Bytes pktmon keeps of each packet. Enough for every header this tool reads.</summary>
    public int CapturePacketSize { get; set; } = 256;
    public long StoreMaxBytes { get; set; } = 200L * 1024 * 1024;
    public int StoreMaxCount { get; set; } = 500_000;
    public int? LastNicComponentId { get; set; }
    public string CxpDeviceNameMatch { get; set; } = "Rapixo";
    /// <summary>Null: look for Matrox.CoaXPress.cti on GENICAM_GENTL64_PATH.</summary>
    public string? GenTLProducerPath { get; set; }
    public List<CxpNodeBinding> CxpNodes { get; set; } = new();
}
