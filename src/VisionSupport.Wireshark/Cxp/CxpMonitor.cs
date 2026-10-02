using VisionSupport.Wireshark.Settings;

namespace VisionSupport.Wireshark.Cxp;

/// <summary>
/// One poll = is the board there, and if it can be read, what do its link/counter nodes say.
/// Every failure to read degrades to presence-only rather than failing: VISION holding the board
/// is the normal case on a running line, not an error.
/// </summary>
public sealed class CxpMonitor : IDisposable
{
    public const string BoardId = "Rapixo#0";
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly Func<(bool Present, string Name)> _presence;
    private readonly Func<IGenTLSession>? _openSession;
    private readonly IReadOnlyList<CxpNodeBinding> _bindings;
    private readonly TimeProvider _clock;
    private Action? _dispose;
    private DateTimeOffset? _failedAt;
    private string _failure = string.Empty;

    public CxpMonitor(Func<(bool Present, string Name)> presence, Func<IGenTLSession>? openSession,
        IReadOnlyList<CxpNodeBinding> bindings, TimeProvider clock)
    {
        _presence = presence;
        _openSession = openSession;
        _bindings = bindings;
        _clock = clock;
    }

    public static CxpMonitor Create(WiresharkSettings settings, TimeProvider clock)
    {
        string? producer = GenTL.FindProducer(settings.GenTLProducerPath);
        GenTL? gentl = null;
        var cache = new Dictionary<string, GenApiNodeMap>();
        bool needDevice = settings.CxpNodes.Any(b => b.Module == "Device");
        Func<IGenTLSession>? open = producer is null
            ? null
            : () => new GenTLSession(gentl ??= new GenTL(producer), cache, needDevice);

        return new CxpMonitor(() => RapixoPresence.Query(settings.CxpDeviceNameMatch), open,
            settings.CxpNodes.ToList(), clock) { _dispose = () => gentl?.Dispose() };
    }

    /// <summary>Never throws: the loop calling this runs for days, and a dead loop would leave a
    /// frozen card that no longer reports an unplugged board.</summary>
    public CxpReport Poll()
    {
        bool present;
        string name;
        try
        {
            (present, name) = _presence();
        }
        catch (Exception ex)
        {
            return PresenceOnly("CXP", $"상세 불가 — 장치 확인 실패: {ex.Message}");
        }
        if (!present) return new CxpReport(BoardId, name, CxpMode.Absent, Array.Empty<CxpConnectionStatus>(), null);
        if (_openSession is null) return PresenceOnly(name, "상세 불가 — GenTL 프로듀서를 찾을 수 없음 (보드 연결 여부만 감시)");
        if (_bindings.Count == 0) return PresenceOnly(name, "상세 불가 — 읽을 노드가 설정되지 않음 (진단 덤프 후 설정)");
        if (_failedAt is { } failed && _clock.GetUtcNow() - failed < RetryAfter) return PresenceOnly(name, _failure);

        try
        {
            IReadOnlyList<NodeReading> readings;
            using (IGenTLSession session = _openSession())
            {
                readings = session.ReadAll(_bindings);
            }
            _failedAt = null;
            return new CxpReport(BoardId, name, CxpMode.Full, Group(readings), null);
        }
        catch (Exception ex)
        {
            _failedAt = _clock.GetUtcNow();
            _failure = $"상세 불가 — {ex.Message} (보드 연결 여부만 감시)";
            return PresenceOnly(name, _failure);
        }
    }

    public void Dispose() => _dispose?.Invoke();

    private static CxpReport PresenceOnly(string name, string message)
        => new(BoardId, name, CxpMode.PresenceOnly, Array.Empty<CxpConnectionStatus>(), message);

    private static IReadOnlyList<CxpConnectionStatus> Group(IReadOnlyList<NodeReading> readings)
        => readings.GroupBy(r => r.Binding.Connection).OrderBy(g => g.Key).Select(g =>
        {
            NodeReading? Find(CxpNodeRole role) => g.FirstOrDefault(r => r.Binding.Role == role);
            return new CxpConnectionStatus(g.Key,
                Find(CxpNodeRole.LinkUp) is { } link ? link.Value != 0 : null,
                Find(CxpNodeRole.Speed)?.Text ?? string.Empty,
                Find(CxpNodeRole.ErrorCount)?.Value,
                Find(CxpNodeRole.FrameCount)?.Value,
                Find(CxpNodeRole.DropCount)?.Value);
        }).ToList();
}
