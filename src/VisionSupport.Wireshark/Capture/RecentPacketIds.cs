namespace VisionSupport.Wireshark.Capture;

/// <summary>pktmon reports one packet at every component edge it crosses; this keeps the first sighting.</summary>
internal sealed class RecentPacketIds
{
    private readonly HashSet<(ulong, uint)> _seen = new();
    private readonly Queue<(ulong, uint)> _order = new();
    private readonly int _capacity;

    public RecentPacketIds(int capacity) => _capacity = capacity;

    public bool Add(ulong group, uint number)
    {
        var key = (group, number);
        if (!_seen.Add(key)) return false;
        _order.Enqueue(key);
        if (_order.Count > _capacity) _seen.Remove(_order.Dequeue());
        return true;
    }
}
