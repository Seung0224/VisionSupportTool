namespace VisionSupport.Wireshark.ViewModels;

/// <summary>One target's history at one sample a second: bytes/s and response time.</summary>
public sealed class ChartHistory
{
    private readonly int _capacity;

    public ChartHistory(int capacity) => _capacity = capacity;

    public List<DateTime> Times { get; } = new();

    /// <summary>When the first sample was taken. Chart X is seconds from here, and it never moves,
    /// so trimming old samples does not slide a window the user has panned to.</summary>
    public DateTime Start { get; private set; }

    public List<double> BytesPerSecond { get; } = new();

    public List<double> ResponseMs { get; } = new();

    public void Add(DateTime time, double bytes, double? responseMs)
    {
        if (Times.Count == 0 && Start == default) Start = time;
        Times.Add(time);
        BytesPerSecond.Add(bytes);
        // A second with no answer keeps the last response time rather than dropping to zero.
        ResponseMs.Add(responseMs ?? (ResponseMs.Count > 0 ? ResponseMs[^1] : 0));
        if (Times.Count <= _capacity) return;
        Times.RemoveAt(0);
        BytesPerSecond.RemoveAt(0);
        ResponseMs.RemoveAt(0);
    }

    public double[] Seconds() => Times.Select(t => (t - Start).TotalSeconds).ToArray();
}
