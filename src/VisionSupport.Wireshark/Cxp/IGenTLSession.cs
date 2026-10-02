using VisionSupport.Wireshark.Settings;

namespace VisionSupport.Wireshark.Cxp;

public sealed record NodeReading(CxpNodeBinding Binding, long Value, string Text);

/// <summary>GenTL handles opened for one poll and closed by Dispose. Never kept between polls.</summary>
public interface IGenTLSession : IDisposable
{
    IReadOnlyList<NodeReading> ReadAll(IReadOnlyList<CxpNodeBinding> bindings);
}
