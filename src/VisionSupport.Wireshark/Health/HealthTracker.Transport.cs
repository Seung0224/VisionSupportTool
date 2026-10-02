using VisionSupport.Wireshark.Cxp;
using VisionSupport.Wireshark.Dissect;

namespace VisionSupport.Wireshark.Health;

public sealed partial class HealthTracker
{
    private TcpAnalyzer _tcp = new();
    private GvspAnalyzer _gvsp = new();
    private readonly Dictionary<string, Dictionary<int, CxpConnectionStatus>> _cxpPrevious = new();
    private readonly List<PendingDrop> _pendingDrops = new();
    private long _captureLostTotal;
    private DateTime? _lastCaptureLoss;

    /// <summary>ETW's cumulative lost-event count. A gap in a video stream within 10 s of a loss
    /// may be ours rather than the camera's, so it is reported as a suspicion, not a drop.</summary>
    public void ReportCaptureLoss(long totalLost)
    {
        lock (_gate)
        {
            if (totalLost > _captureLostTotal) _lastCaptureLoss = Now;
            _captureLostTotal = totalLost;
        }
    }

    public void ReportLink(string nicId, string name, bool up)
    {
        lock (_gate)
        {
            TargetState t = GetOrAdd(nicId, TargetKind.Nic);
            t.Name = name;
            t.LastSeen = Now;
            if (!up && !t.LinkDown)
            {
                t.LinkDownText = "링크 끊김";
                Raise(t, AnomalyKind.LinkDown, HealthLevel.Bad, $"{name} 링크 끊김", null);
            }
            t.LinkDown = !up;
        }
    }

    /// <summary>Total bytes captured on this NIC so far; the NIC card's chart is that traffic.</summary>
    public void ReportNicTraffic(string nicId, long totalBytes)
    {
        lock (_gate)
        {
            if (_targets.TryGetValue(nicId, out TargetState? t)) t.Bytes = totalBytes;
        }
    }

    public void ReportCxp(CxpReport report)
    {
        lock (_gate)
        {
            TargetState t = GetOrAdd(report.BoardId, TargetKind.Cxp);
            t.Name = $"CXP 보드 · {report.BoardName}";
            t.LastSeen = Now;

            switch (report.Mode)
            {
                case CxpMode.Absent:
                    t.StatusOverride = null;
                    if (!t.LinkDown)
                    {
                        Raise(t, AnomalyKind.DeviceRemoved, HealthLevel.Bad, "CXP 보드가 보이지 않음(분리됨)", null);
                    }
                    t.LinkDown = true;
                    t.LinkDownText = "CXP 보드가 보이지 않음(분리됨)";
                    _cxpPrevious.Remove(report.BoardId);
                    return;
                case CxpMode.PresenceOnly:
                    t.LinkDown = false;
                    t.StatusOverride = (HealthLevel.Idle, report.Message ?? "상세 불가 — 보드 연결 여부만 감시");
                    return;
            }

            t.StatusOverride = null;
            int[] down = report.Connections.Where(c => c.LinkUp == false).Select(c => c.Index).ToArray();
            string downText = $"CXP 링크 끊김 (커넥션 {string.Join(",", down)})";
            if (down.Length > 0 && !t.LinkDown) Raise(t, AnomalyKind.LinkDown, HealthLevel.Bad, downText, null);
            t.LinkDown = down.Length > 0;
            t.LinkDownText = downText;

            _cxpPrevious.TryGetValue(report.BoardId, out Dictionary<int, CxpConnectionStatus>? previous);
            foreach (CxpConnectionStatus c in report.Connections)
            {
                if (previous?.GetValueOrDefault(c.Index) is not { } before) continue;
                if (c.ErrorCount > before.ErrorCount)
                {
                    Raise(t, AnomalyKind.CxpErrors, HealthLevel.Warn,
                        $"CXP 커넥션 {c.Index} 에러 +{c.ErrorCount - before.ErrorCount}", null);
                }
                if (c.DropCount > before.DropCount)
                {
                    long delta = c.DropCount!.Value - (before.DropCount ?? 0);
                    t.DropCount += delta;
                    Raise(t, AnomalyKind.FrameDrop, HealthLevel.Bad, $"프레임 드롭 {delta}장 (CXP, 누적 {t.DropCount})", null);
                }
            }
            _cxpPrevious[report.BoardId] = report.Connections.ToDictionary(c => c.Index);
        }
    }

    partial void ObserveTransportCore(TargetState target, Packet packet)
    {
        TcpVerdict verdict = _tcp.Inspect(packet);
        if (verdict.HasFlag(TcpVerdict.Retransmission))
        {
            packet.IsAnomalous = true;
            target.Retransmits.Enqueue(packet.Time);
            if (target.Retransmits.Count == _t.RetransmitsPerMinute)
            {
                Raise(target, AnomalyKind.Retransmit, HealthLevel.Warn, $"재전송 {target.Retransmits.Count}회 (최근 1분)", packet.Number);
            }
        }
        // An unwatched internet peer resets and closes connections as a matter of course.
        bool ordinaryPeer = target.Kind == TargetKind.Host && !target.Pinned;
        if ((verdict & (TcpVerdict.Reset | TcpVerdict.Fin)) != 0 && !ordinaryPeer)
        {
            packet.IsAnomalous = true;
            target.ForgetClient(packet.Source == target.Id ? packet.Destination : packet.Source);
            Raise(target, AnomalyKind.ConnectionClosed, HealthLevel.Bad,
                verdict.HasFlag(TcpVerdict.Reset) ? "연결 리셋(RST)" : "연결 종료(FIN)", packet.Number);
        }
        if (verdict.HasFlag(TcpVerdict.ZeroWindow) && !ordinaryPeer)
        {
            packet.IsAnomalous = true;
            Raise(target, AnomalyKind.ZeroWindow, HealthLevel.Warn, "수신 버퍼 가득 참(제로 윈도우)", packet.Number);
        }

        if (packet.Gvsp is { } gvsp)
        {
            int lost = _gvsp.Inspect(target.Id, gvsp);
            if (lost == 0) return;

            // Judged on the next tick, not here: the ETW lost-event count that could explain this
            // gap is only read once a second, usually after the gap has already been seen.
            packet.IsAnomalous = true;
            _pendingDrops.Add(new PendingDrop(target, lost, packet.Time, packet.Number));
        }
    }

    private void ResetTransport()
    {
        _tcp = new TcpAnalyzer();
        _gvsp = new GvspAnalyzer();
        _pendingDrops.Clear();
    }

    /// <summary>
    /// Settles drops seen since the last tick. A capture loss from 10 s before the gap up to now
    /// makes it a yellow suspicion; otherwise, once a tick has had the chance to report a loss
    /// (2 s), it is a red drop.
    /// </summary>
    private void SettleDrops(DateTime now)
    {
        for (int i = _pendingDrops.Count - 1; i >= 0; i--)
        {
            PendingDrop d = _pendingDrops[i];
            bool explained = _lastCaptureLoss is { } loss && loss >= d.Seen.AddSeconds(-10);
            if (!explained && (now - d.Seen).TotalSeconds < 2) continue;

            _pendingDrops.RemoveAt(i);
            if (explained)
            {
                Raise(d.Target, AnomalyKind.FrameDrop, HealthLevel.Warn, $"프레임 드롭 의심 {d.Lost}장 (캡처 누락 동반)", d.Packet);
            }
            else
            {
                d.Target.DropCount += d.Lost;
                Raise(d.Target, AnomalyKind.FrameDrop, HealthLevel.Bad, $"프레임 드롭 {d.Lost}장 (누적 {d.Target.DropCount})", d.Packet);
            }
        }
    }

    private sealed record PendingDrop(TargetState Target, int Lost, DateTime Seen, long Packet);
}
