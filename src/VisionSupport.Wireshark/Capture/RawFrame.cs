namespace VisionSupport.Wireshark.Capture;

/// <summary>One frame as pktmon delivered it: possibly cut at --pkt-size, tagged with the NIC component.</summary>
public readonly record struct RawFrame(DateTime Time, byte[] Data, int OriginalLength, int ComponentId);
