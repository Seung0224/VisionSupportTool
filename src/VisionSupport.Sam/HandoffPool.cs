namespace VisionSupport.Sam;

/// <summary>
/// A fixed set of buffers passed from one writer thread to reader threads without copying and
/// without allocating.
///
/// The writer rents an item nobody is reading, fills it, and publishes it as current. Readers lease
/// the current item and release it when done. An item goes back to the writer only once it is
/// neither current nor leased - overwriting one that is still being read would put a new picture's
/// pixels under an old picture's mask.
///
/// One writer only: renting twice before publishing simply gives up the first rental.
/// </summary>
internal sealed class HandoffPool<T> where T : class
{
    private readonly object _gate = new();
    private readonly T[] _items;
    private readonly int[] _leases;
    private int _current = -1;

    public HandoffPool(IEnumerable<T> items)
    {
        _items = items.ToArray();
        _leases = new int[_items.Length];
    }

    public IReadOnlyList<T> Items => _items;

    /// <summary>An item that is neither current nor leased, or null when every item is in use -
    /// the writer tries again once a reader lets go.</summary>
    public T? RentForWriting()
    {
        lock (_gate)
        {
            for (int i = 0; i < _items.Length; i++)
            {
                if (i != _current && _leases[i] == 0) return _items[i];
            }
            return null;
        }
    }

    public void Publish(T item)
    {
        lock (_gate)
        {
            _current = IndexOf(item);
        }
    }

    /// <summary>The current item, leased until <see cref="Release"/>; null before the first publish.</summary>
    public T? AcquireCurrent()
    {
        lock (_gate)
        {
            if (_current < 0) return null;
            _leases[_current]++;
            return _items[_current];
        }
    }

    public void Release(T item)
    {
        lock (_gate)
        {
            int index = IndexOf(item);
            if (_leases[index] > 0) _leases[index]--;
        }
    }

    private int IndexOf(T item)
    {
        for (int i = 0; i < _items.Length; i++)
        {
            if (ReferenceEquals(_items[i], item)) return i;
        }
        throw new ArgumentException("풀에 없는 항목입니다.", nameof(item));
    }
}
