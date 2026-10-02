namespace VisionSupport.Wireshark.Cxp;

/// <summary>How much the monitor could see of the board this poll.</summary>
public enum CxpMode
{
    /// <summary>Link state and counters were read through GenTL.</summary>
    Full,
    /// <summary>The board is there, but its registers could not be read (VISION holds it, no producer, ...).</summary>
    PresenceOnly,
    /// <summary>The board is not in the device list at all.</summary>
    Absent,
}

/// <summary>One CXP connection. Null means "not readable on this board", not zero.</summary>
public sealed record CxpConnectionStatus(int Index, bool? LinkUp, string Speed, long? ErrorCount,
    long? FrameCount, long? DropCount);

public sealed record CxpReport(string BoardId, string BoardName, CxpMode Mode,
    IReadOnlyList<CxpConnectionStatus> Connections, string? Message);
