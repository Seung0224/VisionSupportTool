namespace VisionSupport.Wireshark.ViewModels;

/// <summary>One target's last ten minutes at one sample a second: bytes/s and response time.</summary>
public sealed class ChartHistory
{
    private readonly int _capacity;

    public ChartHistory(int capacity) => _capacity = capacity;

    public List<DateTime> Times { get; } = new();

    public List<double> BytesPerSecond { get; } = new();

    public List<double> ResponseMs { get; } = new();

    public void Add(DateTime time, double bytes, double? responseMs)
    {
        Times.Add(time);
        BytesPerSecond.Add(bytes);
        // A second with no answer keeps the last response time rather than dropping to zero.
        ResponseMs.Add(responseMs ?? (ResponseMs.Count > 0 ? ResponseMs[^1] : 0));
        if (Times.Count <= _capacity) return;
        Times.RemoveAt(0);
        BytesPerSecond.RemoveAt(0);
        ResponseMs.RemoveAt(0);
    }
}
