using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.Health;

/// <summary>
/// Turns packets into a verdict per target. Packet arrival updates counters; <see cref="Tick"/>,
/// once a second, does everything that depends on time passing - so "no answer" and "gone quiet"
/// show up even when no packet arrives at all.
///
/// Thread-safe: the capture thread calls <see cref="Observe"/>, the UI timer everything else.
/// </summary>
public sealed partial class HealthTracker
{
    private const int MaxPending = 256;

    /// <summary>A busy internet port meets thousands of peers; the quietest unpinned ones go first.</summary>
    public const int MaxPeers = 300;

    private readonly object _gate = new();
    private readonly Dictionary<string, TargetState> _targets = new();
    private readonly List<Anomaly> _raised = new();
    private readonly HealthThresholds _t;
    private readonly TimeProvider _clock;
    private DateTime _acknowledgedAt = DateTime.MinValue;

    /// <summary>False while the capture is stopped: silence then means nothing about the target.</summary>
    private bool _listening = true;

    /// <summary>This PC's own addresses: the other end of a packet is the peer.</summary>
    private HashSet<System.Net.IPAddress> _local = new();

    public HealthTracker(HealthThresholds thresholds, TimeProvider clock)
    {
        _t = thresholds;
        _clock = clock;
    }

    private DateTime Now => _clock.GetLocalNow().DateTime;

    public static string KindLabel(TargetKind kind) => kind switch
    {
        TargetKind.Mc => "PLC (MC)",
        TargetKind.Ads => "PLC (ADS)",
        TargetKind.GigE => "카메라 (GigE)",
        TargetKind.Nic => "랜카드",
        TargetKind.Cxp => "카메라 (CXP)",
        TargetKind.Host => "일반",
        _ => kind.ToString(),
    };

    public void Pin(string id, TargetKind kind, string name)
    {
        lock (_gate)
        {
            TargetState t = GetOrAdd(id, kind);
            t.Pinned = true;
            t.Name = name;
        }
    }

    public void Unpin(string id)
    {
        lock (_gate)
        {
            if (!_targets.TryGetValue(id, out TargetState? t)) return;
            if (t.LastSeen is null) _targets.Remove(id);
            else t.Pinned = false;
        }
    }

    /// <summary>
    /// The capture stopped. Packet-fed targets read "캡처 꺼짐" instead of going red, requests in
    /// flight are forgotten, and sequence/stream state is dropped so a later restart does not
    /// count the gap as retransmits or lost frames.
    /// </summary>
    public void StopListening()
    {
        lock (_gate)
        {
            _listening = false;
            ResetTransport();
            foreach (TargetState t in _targets.Values)
            {
                t.Correlated.Clear();
                t.Fifo.Clear();
                t.SilenceReported = false;
            }
        }
    }

    public void SetLocalAddresses(IEnumerable<System.Net.IPAddress> addresses)
    {
        lock (_gate) _local = addresses.ToHashSet();
    }

    public void StartListening()
    {
        lock (_gate) _listening = true;
    }

    public void Observe(Packet packet)
    {
        lock (_gate)
        {
            if (packet.SrcIp is not null && _local.Contains(packet.SrcIp)) packet.Direction = PacketDirection.Out;
            else if (packet.DstIp is not null && _local.Contains(packet.DstIp)) packet.Direction = PacketDirection.In;

            TargetState? target = Resolve(packet);
            if (target is null) return;

            packet.TargetId = target.Id;
            target.LastSeen = packet.Time;
            target.LastPacket = packet.Number;
            target.Bytes += packet.OriginalLength;
            target.SilenceReported = false;

            foreach (AppMessage message in packet.Messages)
            {
                if (message.Kind is AppKind.Mc or AppKind.Ads)
                {
                    TrackMessage(target, packet, message);
                }
                else if (message.IsError)
                {
                    packet.IsAnomalous = true;
                    Raise(target, AnomalyKind.ErrorResponse, HealthLevel.Warn, message.Summary, packet.Number);
                }
            }

            ObserveTransportCore(target, packet);
        }
    }

    /// <summary>TCP and GVSP analysis, implemented by Task 9 in HealthTracker.Transport.cs. Called under the lock.</summary>
    partial void ObserveTransportCore(TargetState target, Packet packet);

    public void Tick()
    {
        lock (_gate)
        {
            DateTime now = Now;
            SettleDrops(now);
            foreach (TargetState t in _targets.Values)
            {
                CheckTimeouts(t, now);
                CheckSilence(t, now);
                Prune(t.Slow, now);
                Prune(t.Retransmits, now);
                Evaluate(t, now);
            }
        }
    }

    public IReadOnlyList<TargetSnapshot> Snapshot()
    {
        lock (_gate)
        {
            // Watched first, then PLCs and cameras, then everyone else by how much they carry.
            return _targets.Values
                .OrderByDescending(t => t.Pinned)
                .ThenBy(t => t.Kind == TargetKind.Host)
                .ThenByDescending(t => t.Bytes)
                .ThenBy(t => t.Id, StringComparer.Ordinal)
                .Select(t => new TargetSnapshot(t.Id, t.Kind, t.Name, t.Pinned, t.Level, t.Summary,
                    t.DropCount, t.LastResponseMs, t.Bytes, t.Detail))
                .ToList();
        }
    }

    public IReadOnlyList<Anomaly> DrainAnomalies()
    {
        lock (_gate)
        {
            List<Anomaly> drained = _raised.ToList();
            _raised.Clear();
            return drained;
        }
    }

    /// <summary>A watched target is red, and went red (or got a new problem) after the last acknowledge.</summary>
    public bool HasAlert
    {
        get
        {
            lock (_gate)
            {
                return _targets.Values.Any(t => t.Watched && t.Level == HealthLevel.Bad
                    && Later(t.BadSince, t.LastBadEvent) > _acknowledgedAt);
            }
        }
    }

    public void Acknowledge()
    {
        lock (_gate) _acknowledgedAt = Now;
    }

    private static DateTime Later(DateTime? a, DateTime? b)
    {
        DateTime x = a ?? DateTime.MinValue, y = b ?? DateTime.MinValue;
        return x > y ? x : y;
    }

    private TargetState GetOrAdd(string id, TargetKind kind, string? name = null)
    {
        if (!_targets.TryGetValue(id, out TargetState? t))
        {
            t = new TargetState(id, kind, name ?? id) { Detail = KindLabel(kind) };
            _targets.Add(id, t);
        }
        return t;
    }

    private TargetState? Find(string id) => _targets.GetValueOrDefault(id);

    private TargetState? Resolve(Packet p)
    {
        if (p.App is { Kind: AppKind.Mc or AppKind.Ads } app && p.Tcp is { } tcp)
        {
            bool toPlc = app.Role == MessageRole.Request;
            string id = toPlc ? $"{p.DstIp}:{tcp.DstPort}" : $"{p.SrcIp}:{tcp.SrcPort}";
            return GetOrAdd(id, app.Kind == AppKind.Mc ? TargetKind.Mc : TargetKind.Ads,
                DeviceName(p, toPlc ? p.DstIp : p.SrcIp));
        }
        if (p.App is { Kind: AppKind.Gvcp } gvcp)
        {
            // A command may go to a broadcast address ("any camera out there?"), so only a host
            // that answers becomes a camera card; commands just reach cameras already known.
            return gvcp.Role == MessageRole.Request
                ? p.DstIp is null ? null : Find(p.DstIp.ToString())
                : p.SrcIp is null ? null : GetOrAdd(p.SrcIp.ToString(), TargetKind.GigE, DeviceName(p, p.SrcIp));
        }
        if (p.Gvsp is not null && p.SrcIp is not null)
        {
            return GetOrAdd(p.SrcIp.ToString(), TargetKind.GigE, DeviceName(p, p.SrcIp));
        }
        if (p.Tcp is { } t && p.SrcIp is not null && p.DstIp is not null
            && (Find($"{p.DstIp}:{t.DstPort}") ?? Find($"{p.SrcIp}:{t.SrcPort}")) is { } device)
        {
            return device;
        }
        return ResolvePeer(p);
    }

    /// <summary>Anything else: the other end of the packet, unless that is a broadcast or group address.</summary>
    private TargetState? ResolvePeer(Packet p)
    {
        if (_local.Count == 0 || p.SrcIp is null || p.DstIp is null) return null;

        bool outgoing = _local.Contains(p.SrcIp);
        System.Net.IPAddress? peer = outgoing ? p.DstIp : _local.Contains(p.DstIp) ? p.SrcIp : null;
        if (peer is null || IsGroupAddress(peer)) return null;

        string id = peer.ToString();
        if (!_targets.ContainsKey(id)) EvictQuietestPeer();
        TargetState target = GetOrAdd(id, TargetKind.Host, id);
        int? port = p.Tcp is { } tcp ? (outgoing ? tcp.DstPort : tcp.SrcPort)
            : p.Udp is { } udp ? (outgoing ? udp.DstPort : udp.SrcPort) : null;
        target.Detail = port is { } n ? $"{(p.Tcp is not null ? "TCP" : "UDP")} {n}" : p.Protocol;
        return target;
    }

    private static bool IsGroupAddress(System.Net.IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        return b.Length != 4 || b[3] == 255 || b[0] >= 224 || b[0] == 0;
    }

    private void EvictQuietestPeer()
    {
        List<TargetState> peers = _targets.Values.Where(t => t.Kind == TargetKind.Host).ToList();
        if (peers.Count < MaxPeers) return;
        TargetState? quietest = peers.Where(t => !t.Pinned).MinBy(t => t.LastSeen ?? DateTime.MinValue);
        if (quietest is not null) _targets.Remove(quietest.Id);
    }

    /// <summary>"PLC 연결 · 192.168.0.10": which of the user's connections, then which device on it.</summary>
    private static string DeviceName(Packet p, System.Net.IPAddress? host)
        => p.Interface is { Length: > 0 } nic ? $"{nic} 연결 · {host}" : $"{host}";

    private void TrackMessage(TargetState target, Packet packet, AppMessage app)
    {
        // Answers pair within one TCP connection: VISION may hold several to the same PLC port,
        // and MC 3E has no serial, so pairing across connections would match the wrong request.
        string client = app.Role == MessageRole.Request ? packet.Source : packet.Destination;
        switch (app.Role)
        {
            case MessageRole.Request:
            {
                var pending = new Pending(packet.Time, packet.Number);
                if (app.CorrelationId is uint id) target.Correlated[$"{client}#{id}"] = pending;
                else target.FifoFor(client).Enqueue(pending);
                target.TrimPending(MaxPending);
                break;
            }
            case MessageRole.Response:
            {
                Pending? matched = null;
                if (app.CorrelationId is uint id) target.Correlated.Remove($"{client}#{id}", out matched);
                else if (target.Fifo.TryGetValue(client, out Queue<Pending>? queue) && queue.Count > 0) matched = queue.Dequeue();

                if (matched is not null)
                {
                    double ms = (packet.Time - matched.Sent).TotalMilliseconds;
                    target.LastResponseMs = ms;
                    if (ms > _t.SlowResponseMs)
                    {
                        packet.IsAnomalous = true;
                        target.Slow.Enqueue(packet.Time);
                        if (target.Slow.Count == _t.SlowPerMinute)
                        {
                            Raise(target, AnomalyKind.SlowResponse, HealthLevel.Warn,
                                $"응답 지연 {target.Slow.Count}회 (최근 1분, 마지막 {ms:0}ms)", packet.Number);
                        }
                    }
                }
                if (app.IsError)
                {
                    packet.IsAnomalous = true;
                    Raise(target, AnomalyKind.ErrorResponse, HealthLevel.Warn, app.Summary, packet.Number);
                }
                break;
            }
        }
    }

    private void CheckTimeouts(TargetState t, DateTime now)
    {
        if (!_listening) return;
        foreach (Pending p in t.AllPending())
        {
            if (p.Reported || (now - p.Sent).TotalMilliseconds < _t.ResponseTimeoutMs) continue;
            p.Reported = true;
            Raise(t, AnomalyKind.Timeout, HealthLevel.Bad, $"응답 없음 (요청 #{p.Packet})", p.Packet);
        }

        // An answer this late is not coming. Let it go so the card can recover; the timeout
        // stays in the anomaly list.
        TimeSpan giveUp = TimeSpan.FromMilliseconds(_t.ResponseTimeoutMs) + TimeSpan.FromSeconds(_t.RecoverySeconds);
        foreach (string key in t.Correlated.Where(kv => kv.Value.Reported && now - kv.Value.Sent > giveUp)
                     .Select(kv => kv.Key).ToList())
        {
            t.Correlated.Remove(key);
        }
        foreach (Queue<Pending> queue in t.Fifo.Values)
        {
            while (queue.Count > 0 && queue.Peek().Reported && now - queue.Peek().Sent > giveUp) queue.Dequeue();
        }
    }

    private void CheckSilence(TargetState t, DateTime now)
    {
        if (!_listening || !t.Pinned || t.Kind is TargetKind.Nic or TargetKind.Cxp) return;
        if (t.LastSeen is not { } seen || t.SilenceReported) return;
        if ((now - seen).TotalSeconds < _t.SilenceSeconds) return;
        t.SilenceReported = true;
        Raise(t, AnomalyKind.Silence, HealthLevel.Bad, $"트래픽 끊김 ({_t.SilenceSeconds}초 이상)", t.LastPacket);
    }

    private static void Prune(Queue<DateTime> times, DateTime now)
    {
        while (times.Count > 0 && (now - times.Peek()).TotalSeconds > 60) times.Dequeue();
    }

    private void Evaluate(TargetState t, DateTime now)
    {
        TimeSpan recovery = TimeSpan.FromSeconds(_t.RecoverySeconds);
        TimeSpan unanswered = t.AllPending().Where(p => p.Reported)
            .Select(p => now - p.Sent).DefaultIfEmpty(TimeSpan.Zero).Max();

        (HealthLevel level, string summary) = true switch
        {
            _ when !_listening && t.Kind is not TargetKind.Cxp => (HealthLevel.Idle, "캡처 꺼짐"),
            _ when t.LinkDown => (HealthLevel.Bad, t.LinkDownText),
            _ when unanswered > TimeSpan.Zero => (HealthLevel.Bad, $"응답 없음 {unanswered.TotalSeconds:0.0}초째"),
            _ when t.SilenceReported && t.LastSeen is { } s => (HealthLevel.Bad, $"트래픽 끊김 {(now - s).TotalSeconds:0}초째"),
            _ when t.LastBadEvent is { } b && now - b < recovery => (HealthLevel.Bad, t.LastBadText),
            _ when t.Slow.Count >= _t.SlowPerMinute => (HealthLevel.Warn, $"최근 1분 응답 지연 {t.Slow.Count}회"),
            _ when t.Retransmits.Count >= _t.RetransmitsPerMinute => (HealthLevel.Warn, $"최근 1분 재전송 {t.Retransmits.Count}회"),
            _ when t.LastWarnEvent is { } w && now - w < recovery => (HealthLevel.Warn, t.LastWarnText),
            _ when t.StatusOverride is { } o => o,
            _ when t.LastSeen is null => (HealthLevel.Idle, "트래픽 없음"),
            _ when t.Kind == TargetKind.Nic => (HealthLevel.Ok, "연결됨"),
            _ => (HealthLevel.Ok, t.LastResponseMs is { } ms ? $"정상 · 응답 {ms:0}ms" : "정상"),
        };

        if (level == HealthLevel.Bad && t.Level != HealthLevel.Bad) t.BadSince = now;
        if (level != HealthLevel.Bad) t.BadSince = null;
        t.Level = level;
        t.Summary = summary;
    }

    private void Raise(TargetState t, AnomalyKind kind, HealthLevel severity, string text, long? packetNumber)
    {
        DateTime now = Now;
        _raised.Add(new Anomaly(now, t.Id, t.Name, kind, severity, text, packetNumber));
        if (severity == HealthLevel.Bad)
        {
            t.LastBadEvent = now;
            t.LastBadText = text;
        }
        else
        {
            t.LastWarnEvent = now;
            t.LastWarnText = text;
        }
    }

    private sealed class Pending
    {
        public Pending(DateTime sent, long packet)
        {
            Sent = sent;
            Packet = packet;
        }

        public DateTime Sent { get; }
        public long Packet { get; }
        public bool Reported { get; set; }
    }

    private sealed class TargetState
    {
        public TargetState(string id, TargetKind kind, string name)
        {
            Id = id;
            Kind = kind;
            Name = name;
        }

        public string Id { get; }
        public TargetKind Kind { get; }
        public string Name { get; set; }
        public string Detail { get; set; } = string.Empty;
        public bool Pinned { get; set; }
        /// <summary>Counts toward the launcher alert: pinned, or the machine's own NIC/board.</summary>
        public bool Watched => Pinned || Kind is TargetKind.Nic or TargetKind.Cxp;

        public DateTime? LastSeen { get; set; }
        public long LastPacket { get; set; }
        public long Bytes { get; set; }
        public double? LastResponseMs { get; set; }
        public long DropCount { get; set; }

        public Dictionary<string, Pending> Correlated { get; } = new();
        public Dictionary<string, Queue<Pending>> Fifo { get; } = new();
        public Queue<DateTime> Slow { get; } = new();
        public Queue<DateTime> Retransmits { get; } = new();

        public bool SilenceReported { get; set; }
        public bool LinkDown { get; set; }
        public string LinkDownText { get; set; } = "링크 끊김";
        public DateTime? LastBadEvent { get; set; }
        public string LastBadText { get; set; } = string.Empty;
        public DateTime? LastWarnEvent { get; set; }
        public string LastWarnText { get; set; } = string.Empty;
        /// <summary>Set by CXP presence-only mode: grey card with the reason instead of "트래픽 없음".</summary>
        public (HealthLevel, string)? StatusOverride { get; set; }

        public HealthLevel Level { get; set; } = HealthLevel.Idle;
        public string Summary { get; set; } = "트래픽 없음";
        public DateTime? BadSince { get; set; }

        public Queue<Pending> FifoFor(string client)
        {
            if (!Fifo.TryGetValue(client, out Queue<Pending>? queue))
            {
                queue = new Queue<Pending>();
                Fifo.Add(client, queue);
            }
            return queue;
        }

        /// <summary>A connection that was reset or closed will never answer what it was asked.</summary>
        public void ForgetClient(string client)
        {
            foreach (string key in Correlated.Keys.Where(k => k.StartsWith(client + "#", StringComparison.Ordinal)).ToList())
            {
                Correlated.Remove(key);
            }
            Fifo.Remove(client);
        }

        public IEnumerable<Pending> AllPending() => Correlated.Values.Concat(Fifo.Values.SelectMany(q => q));

        /// <summary>Requests that never get an answer must not pile up for days.</summary>
        public void TrimPending(int max)
        {
            while (Correlated.Count > max)
            {
                Correlated.Remove(Correlated.MinBy(kv => kv.Value.Sent).Key);
            }
            foreach (Queue<Pending> queue in Fifo.Values)
            {
                while (queue.Count > max) queue.Dequeue();
            }
            if (Fifo.Count > max) Fifo.Clear();
        }
    }
}
