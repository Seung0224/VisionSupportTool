using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.Store;

/// <summary>
/// Recent packets in a ring capped by count and bytes, plus the packets around every anomaly,
/// kept after the ring has moved on - the evidence is what you want when you open the window an
/// hour after the PLC hiccupped. Kept packets are reference-counted because two anomalies close
/// together share their neighbours.
/// </summary>
public sealed class PacketStore
{
    private readonly object _gate = new();
    private readonly LinkedList<Packet> _ring = new();
    private readonly Dictionary<long, (Packet Packet, int Refs)> _kept = new();
    private readonly Queue<List<long>> _events = new();
    private readonly long _maxBytes;
    private readonly int _maxCount, _keepBefore, _keepAfter, _maxKeptEvents;
    private List<long>? _collecting;
    private int _afterRemaining;
    private long _ringBytes;

    public PacketStore(long maxBytes, int maxCount, int keepBefore = 20, int keepAfter = 20, int maxKeptEvents = 1000)
    {
        _maxBytes = maxBytes;
        _maxCount = maxCount;
        _keepBefore = keepBefore;
        _keepAfter = keepAfter;
        _maxKeptEvents = maxKeptEvents;
    }

    public int Count
    {
        get { lock (_gate) return _ring.Count + _kept.Values.Count(k => !InRing(k.Packet)); }
    }

    public long Bytes
    {
        get { lock (_gate) return _ringBytes; }
    }

    public void Add(Packet packet)
    {
        lock (_gate)
        {
            _ring.AddLast(packet);
            _ringBytes += packet.Data.Length;

            if (_afterRemaining > 0 && _collecting is not null)
            {
                Keep(packet, _collecting);
                _afterRemaining--;
            }

            while ((_ringBytes > _maxBytes || _ring.Count > _maxCount) && _ring.First is { } first)
            {
                _ringBytes -= first.Value.Data.Length;
                _ring.RemoveFirst();
            }
        }
    }

    public void KeepAround(long packetNumber)
    {
        lock (_gate)
        {
            var ids = new List<long>();
            int taken = 0;
            for (LinkedListNode<Packet>? n = _ring.Last; n is not null && taken <= _keepBefore; n = n.Previous)
            {
                if (n.Value.Number > packetNumber) continue;
                Keep(n.Value, ids);
                taken++;
            }

            _events.Enqueue(ids);
            _collecting = ids;
            _afterRemaining = _keepAfter;

            while (_events.Count > _maxKeptEvents)
            {
                foreach (long id in _events.Dequeue()) Release(id);
            }
        }
    }

    public IReadOnlyList<Packet> Snapshot()
    {
        lock (_gate)
        {
            return _kept.Values.Select(k => k.Packet).Where(p => !InRing(p))
                .OrderBy(p => p.Number)
                .Concat(_ring)
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _ring.Clear();
            _kept.Clear();
            _events.Clear();
            _collecting = null;
            _afterRemaining = 0;
            _ringBytes = 0;
        }
    }

    private bool InRing(Packet p) => _ring.First is { } first && p.Number >= first.Value.Number;

    private void Keep(Packet packet, List<long> owner)
    {
        owner.Add(packet.Number);
        _kept[packet.Number] = _kept.TryGetValue(packet.Number, out var k) ? (k.Packet, k.Refs + 1) : (packet, 1);
    }

    private void Release(long id)
    {
        if (!_kept.TryGetValue(id, out var k)) return;
        if (k.Refs <= 1) _kept.Remove(id);
        else _kept[id] = (k.Packet, k.Refs - 1);
    }
}
