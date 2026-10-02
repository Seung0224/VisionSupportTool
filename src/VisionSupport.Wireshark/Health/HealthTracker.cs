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

    private readonly object _gate = new();
    private readonly Dictionary<string, TargetState> _targets = new();
    private readonly List<Anomaly> _raised = new();
    private readonly HealthThresholds _t;
    private readonly TimeProvider _clock;
    private DateTime _acknowledgedAt = DateTime.MinValue;

    public HealthTracker(HealthThresholds thresholds, TimeProvider clock)
    {
        _t = thresholds;
        _clock = clock;
    }

    private DateTime Now => _clock.GetLocalNow().DateTime;

    public static string KindLabel(TargetKind kind) => kind switch
    {
        TargetKind.Mc => "PLC(MC)",
        TargetKind.Ads => "PLC(ADS)",
        TargetKind.GigE => "카메라(GigE)",
        TargetKind.Nic => "네트워크",
        TargetKind.Cxp => "카메라(CXP)",
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

    public void Observe(Packet packet)
    {
        lock (_gate)
        {
            TargetState? target = Resolve(packet);
            if (target is null) return;

            packet.TargetId = target.Id;
            target.LastSeen = packet.Time;
            target.LastPacket = packet.Number;
            target.Bytes += packet.OriginalLength;
            target.SilenceReported = false;

            if (packet.App is { Kind: AppKind.Mc or AppKind.Ads } app)
            {
                TrackMessage(target, packet, app);
            }
            else if (packet.App is { IsError: true } other)
            {
                packet.IsAnomalous = true;
                Raise(target, AnomalyKind.ErrorResponse, HealthLevel.Warn, other.Summary, packet.Number);
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
            return _targets.Values
                .OrderByDescending(t => t.Pinned).ThenBy(t => t.Kind).ThenBy(t => t.Id, StringComparer.Ordinal)
                .Select(t => new TargetSnapshot(t.Id, t.Kind, t.Name, t.Pinned, t.Level, t.Summary,
                    t.DropCount, t.LastResponseMs, t.Bytes))
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

    private TargetState GetOrAdd(string id, TargetKind kind)
    {
        if (!_targets.TryGetValue(id, out TargetState? t))
        {
            t = new TargetState(id, kind, $"{KindLabel(kind)} {id}");
            _targets.Add(id, t);
        }
        return t;
    }

    private TargetState? Find(string id) => _targets.GetValueOrDefault(id);

    private TargetState? Resolve(Packet p)
    {
        if (p.App is { Kind: AppKind.Mc or AppKind.Ads } app && p.Tcp is { } tcp)
        {
            string id = app.Role == MessageRole.Request ? $"{p.DstIp}:{tcp.DstPort}" : $"{p.SrcIp}:{tcp.SrcPort}";
            return GetOrAdd(id, app.Kind == AppKind.Mc ? TargetKind.Mc : TargetKind.Ads);
        }
        if (p.App is { Kind: AppKind.Gvcp } gvcp)
        {
            var camera = gvcp.Role == MessageRole.Request ? p.DstIp : p.SrcIp;
            return camera is null ? null : GetOrAdd(camera.ToString(), TargetKind.GigE);
        }
        if (p.Gvsp is not null && p.SrcIp is not null) return GetOrAdd(p.SrcIp.ToString(), TargetKind.GigE);
        if (p.Tcp is { } t && p.SrcIp is not null && p.DstIp is not null)
        {
            return Find($"{p.DstIp}:{t.DstPort}") ?? Find($"{p.SrcIp}:{t.SrcPort}");
        }
        return null;
    }

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
        foreach (Pending p in t.AllPending())
        {
            if (p.Reported || (now - p.Sent).TotalMilliseconds < _t.ResponseTimeoutMs) continue;
            p.Reported = true;
            Raise(t, AnomalyKind.Timeout, HealthLevel.Bad, $"응답 없음 (요청 #{p.Packet})", p.Packet);
        }
    }

    private void CheckSilence(TargetState t, DateTime now)
    {
        if (!t.Pinned || t.Kind is TargetKind.Nic or TargetKind.Cxp) return;
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
            _ when t.LinkDown => (HealthLevel.Bad, t.LinkDownText),
            _ when unanswered > TimeSpan.Zero => (HealthLevel.Bad, $"응답 없음 {unanswered.TotalSeconds:0.0}초째"),
            _ when t.SilenceReported && t.LastSeen is { } s => (HealthLevel.Bad, $"트래픽 끊김 {(now - s).TotalSeconds:0}초째"),
            _ when t.LastBadEvent is { } b && now - b < recovery => (HealthLevel.Bad, t.LastBadText),
            _ when t.Slow.Count >= _t.SlowPerMinute => (HealthLevel.Warn, $"최근 1분 응답 지연 {t.Slow.Count}회"),
            _ when t.Retransmits.Count >= _t.RetransmitsPerMinute => (HealthLevel.Warn, $"최근 1분 재전송 {t.Retransmits.Count}회"),
            _ when t.LastWarnEvent is { } w && now - w < recovery => (HealthLevel.Warn, t.LastWarnText),
            _ when t.StatusOverride is { } o => o,
            _ when t.LastSeen is null => (HealthLevel.Idle, "트래픽 없음"),
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
