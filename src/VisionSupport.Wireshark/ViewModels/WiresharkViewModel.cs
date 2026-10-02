using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSupport.Wireshark.Capture;
using VisionSupport.Wireshark.Cxp;
using VisionSupport.Wireshark.Dissect;
using VisionSupport.Wireshark.Export;
using VisionSupport.Wireshark.Health;
using VisionSupport.Wireshark.Settings;
using VisionSupport.Wireshark.Store;

namespace VisionSupport.Wireshark.ViewModels;

/// <summary>
/// Wires capture → dissect → judge → store on the capture thread, and shows the result on two UI
/// timers: 200 ms for new rows (so a burst never floods the dispatcher), 1 s for everything that
/// depends on time passing (cards, alert, chart). Lives on after the window closes while capture
/// or CXP watching is running - the feature keeps it, the view is rebuilt.
/// </summary>
public sealed partial class WiresharkViewModel : ObservableObject, IDisposable
{
    private const int MaxRows = 20_000;
    private const int MaxAnomalies = 1000;
    private const int MaxDrainPerTick = 5_000;
    private const int MaxQueued = 100_000;
    private const int ChartSeconds = 600;

    private readonly WiresharkSettingsStore _store;
    private readonly WiresharkSettings _settings;
    private readonly TimeProvider _clock;
    private readonly HealthTracker _health;
    private readonly PacketStore _packets;
    private readonly ConcurrentQueue<Packet> _incoming = new();
    private readonly Dictionary<string, TargetCardViewModel> _cards = new();
    private readonly Dictionary<string, ChartHistory> _history = new();
    private readonly Dictionary<string, long> _lastBytes = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _rowTimer;
    private readonly DispatcherTimer _tickTimer;
    private DissectorContext _context = new();
    private PktmonSource? _capture;
    private CxpMonitor? _cxp;
    private CancellationTokenSource? _cxpStop;
    private Task? _cxpLoop;
    private volatile int _nicFilter = -1;
    private long _nextNumber;
    private bool _disposed;

    public WiresharkViewModel() : this(WiresharkSettingsStore.Default, TimeProvider.System)
    {
    }

    public WiresharkViewModel(WiresharkSettingsStore store, TimeProvider clock)
    {
        _store = store;
        _settings = store.Load();
        _clock = clock;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _health = new HealthTracker(_settings.Thresholds, clock);
        foreach (PinnedTarget p in _settings.Pinned) _health.Pin(p.Id, p.Kind, p.Name);
        _packets = new PacketStore(_settings.StoreMaxBytes, _settings.StoreMaxCount);

        _rowTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => DrainIncoming(), _dispatcher);
        _tickTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => OnTick(), _dispatcher);
        OnTick();
    }

    public ObservableCollection<NicInfo> Nics { get; } = new();

    public ObservableCollection<TargetCardViewModel> Cards { get; } = new();

    public ObservableCollection<Anomaly> Anomalies { get; } = new();

    public ObservableCollection<CxpConnectionStatus> CxpConnections { get; } = new();

    /// <summary>Replaced wholesale when trimmed: removing from the front one by one is O(n) each.</summary>
    [ObservableProperty] private ObservableCollection<Packet> _rows = new();

    [ObservableProperty] private NicInfo? _selectedNic;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private Packet? _selectedPacket;
    [ObservableProperty] private IReadOnlyList<ProtocolNode>? _selectedLayers;
    [ObservableProperty] private string _hexDump = string.Empty;
    [ObservableProperty] private Anomaly? _selectedAnomaly;
    [ObservableProperty] private TargetCardViewModel? _focusedCard;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWorking))] private bool _isCapturing;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWorking))] private bool _isCxpWatching;
    [ObservableProperty] private bool _hasAlert;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private string _captureError = string.Empty;
    [ObservableProperty] private string _cxpMessage = string.Empty;

    /// <summary>The one answer to "is anything wrong?", shown before any card.</summary>
    [ObservableProperty] private HealthLevel _overallLevel;
    [ObservableProperty] private string _overallText = "감시 대상 없음";

    public bool IsWorking => IsCapturing || IsCxpWatching;

    public ChartHistory? FocusedHistory => FocusedCard is { } c ? _history.GetValueOrDefault(c.Id) : null;

    /// <summary>Raised once a second after the chart data moved.</summary>
    public event EventHandler? ChartUpdated;

    partial void OnSelectedNicChanged(NicInfo? value)
    {
        _nicFilter = value?.ComponentId ?? -1;
        _settings.LastNicComponentId = value?.ComponentId;
    }

    partial void OnFilterTextChanged(string value) => RebuildRows();

    partial void OnSelectedPacketChanged(Packet? value)
    {
        SelectedLayers = value?.Layers;
        HexDump = value is null ? string.Empty : HexFormatter.Format(value.Data);
    }

    partial void OnSelectedAnomalyChanged(Anomaly? value)
    {
        if (value?.PacketNumber is not long number) return;
        Packet? packet = Rows.FirstOrDefault(p => p.Number == number)
            ?? _packets.Snapshot().FirstOrDefault(p => p.Number == number);
        if (packet is null) return;
        if (!Rows.Contains(packet))
        {
            FilterText = string.Empty;
        }
        SelectedPacket = Rows.FirstOrDefault(p => p.Number == number);
    }

    partial void OnFocusedCardChanged(TargetCardViewModel? value) => OnPropertyChanged(nameof(FocusedHistory));

    [RelayCommand]
    private void RefreshNics()
    {
        try
        {
            Nics.Clear();
            foreach (NicInfo nic in NicCatalog.Query()) Nics.Add(nic);
            SelectedNic = Nics.FirstOrDefault(n => n.ComponentId == _settings.LastNicComponentId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            CaptureError = "NIC 목록을 읽을 수 없음: " + ex.Message;
        }
    }

    [RelayCommand]
    private void StartCapture()
    {
        if (IsCapturing) return;
        CaptureError = string.Empty;
        _context = new DissectorContext { McPortMin = _settings.McPortMin, McPortMax = _settings.McPortMax };

        var source = new PktmonSource(OnFrame);
        source.Faulted += message => _dispatcher.BeginInvoke(() =>
        {
            CaptureError = "캡처 중단: " + message;
            StopCapture();
        });
        try
        {
            source.Start(_settings.CapturePacketSize);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            source.Dispose();
            CaptureError = "캡처를 시작할 수 없음: " + ex.Message;
            return;
        }
        _health.StartListening();
        _capture = source;
        IsCapturing = true;
    }

    [RelayCommand]
    private void StopCapture()
    {
        _capture?.Dispose();
        _capture = null;
        _health.StopListening();
        IsCapturing = false;
    }

    [RelayCommand]
    private void StartCxp()
    {
        if (IsCxpWatching) return;
        CxpMonitor monitor = CxpMonitor.Create(_settings, _clock);
        var stop = new CancellationTokenSource();
        _cxp = monitor;
        _cxpStop = stop;
        IsCxpWatching = true;
        _cxpLoop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                do
                {
                    CxpReport report = monitor.Poll();
                    _health.ReportCxp(report);
                    _ = _dispatcher.BeginInvoke(() => ShowCxp(report));
                }
                while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                // Unloaded here, never from the UI thread: a stop that gives up waiting must not
                // free the producer DLL while a poll is still inside it.
                monitor.Dispose();
            }
        });
    }

    [RelayCommand]
    private void StopCxp()
    {
        _cxpStop?.Cancel();
        try
        {
            _cxpLoop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }
        _cxpStop = null;
        _cxpLoop = null;
        _cxp = null;
        IsCxpWatching = false;
        CxpConnections.Clear();
    }

    [RelayCommand]
    private void Acknowledge()
    {
        _health.Acknowledge();
        HasAlert = false;
    }

    [RelayCommand]
    private void TogglePin(TargetCardViewModel card)
    {
        if (!card.CanPin) return;
        if (card.Pinned)
        {
            _health.Unpin(card.Id);
            _settings.Pinned.RemoveAll(p => p.Id == card.Id);
        }
        else
        {
            _health.Pin(card.Id, card.Kind, card.Name);
            _settings.Pinned.Add(new PinnedTarget { Id = card.Id, Kind = card.Kind, Name = card.Name });
        }
        SaveSettings();
        OnTick();
    }

    [RelayCommand]
    private void FocusCard(TargetCardViewModel card)
    {
        FocusedCard = card;
        FilterText = "id:" + card.Id;
        SelectedPacket = Rows.LastOrDefault(p => p.IsAnomalous) ?? Rows.LastOrDefault();
        ChartUpdated?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearFilter()
    {
        FocusedCard = null;
        FilterText = string.Empty;
    }

    [RelayCommand]
    private void ClearPackets()
    {
        _packets.Clear();
        Rows = new ObservableCollection<Packet>();
        SelectedPacket = null;
    }

    /// <returns>How many packets were written.</returns>
    public int ExportTo(string path)
    {
        IReadOnlyList<Packet> all = _packets.Snapshot();
        using FileStream file = File.Create(path);
        PcapngWriter.Write(file, all);
        return all.Count;
    }

    /// <returns>The path of the written report.</returns>
    public string WriteCxpDiagnostics()
    {
        string text = CxpDiagnostics.Run(_settings);
        Directory.CreateDirectory(_store.DataDirectory);
        string path = Path.Combine(_store.DataDirectory, $"cxp-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _rowTimer.Stop();
        _tickTimer.Stop();
        StopCapture();
        StopCxp();
        SaveSettings();
    }

    /// <summary>Capture thread. Dissect and judge here so the UI only ever sees finished packets.</summary>
    private void OnFrame(RawFrame frame)
    {
        int nic = _nicFilter;
        if (nic >= 0 && frame.ComponentId != nic) return;

        long number = Interlocked.Increment(ref _nextNumber);
        Packet packet = FrameDissector.Dissect(number, frame.Time, frame.Data, frame.OriginalLength, _context);
        _health.Observe(packet);

        // Spec §8: video payload is judged, not kept - except the packets that showed a problem.
        if (packet.Gvsp is { Format: GvspFormat.Payload } && !packet.IsAnomalous) return;

        _packets.Add(packet);
        if (_incoming.Count < MaxQueued) _incoming.Enqueue(packet);
    }

    private void DrainIncoming()
    {
        var filter = new PacketFilter(FilterText);
        for (int i = 0; i < MaxDrainPerTick && _incoming.TryDequeue(out Packet? p); i++)
        {
            if (filter.Matches(p)) Rows.Add(p);
        }
        if (Rows.Count > MaxRows + MaxRows / 10)
        {
            Rows = new ObservableCollection<Packet>(Rows.Skip(Rows.Count - MaxRows));
        }

        foreach (Anomaly a in _health.DrainAnomalies())
        {
            if (a.PacketNumber is long n) _packets.KeepAround(n);
            Anomalies.Insert(0, a);
        }
        while (Anomalies.Count > MaxAnomalies) Anomalies.RemoveAt(Anomalies.Count - 1);
    }

    private void RebuildRows()
    {
        var filter = new PacketFilter(FilterText);
        List<Packet> matching = _packets.Snapshot().Where(filter.Matches).ToList();
        Rows = new ObservableCollection<Packet>(matching.Skip(Math.Max(0, matching.Count - MaxRows)));
    }

    private void OnTick()
    {
        if (_capture is { } capture)
        {
            _health.ReportCaptureLoss(capture.EventsLost);
            if (SelectedNic is { } nic) _health.ReportLink(nic.Mac, nic.Name, NicCatalog.IsUp(nic.Mac) ?? false);
        }
        _health.Tick();
        UpdateCards();
        DrainIncoming();

        HasAlert = _health.HasAlert;
        UpdateOverall();
        Status = !IsWorking ? string.Empty
            : $"패킷 {Interlocked.Read(ref _nextNumber):N0} · 보관 {_packets.Count:N0}"
              + (_capture is { EventsLost: > 0 } c ? $" · 캡처 누락 {c.EventsLost:N0}" : string.Empty)
              + (HasAlert ? " · 이상 있음" : string.Empty);
        ChartUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateCards()
    {
        DateTime now = _clock.GetLocalNow().DateTime;
        IReadOnlyList<TargetSnapshot> snapshots = _health.Snapshot();
        var seen = new HashSet<string>();

        for (int i = 0; i < snapshots.Count; i++)
        {
            TargetSnapshot s = snapshots[i];
            seen.Add(s.Id);
            if (!_cards.TryGetValue(s.Id, out TargetCardViewModel? card))
            {
                card = new TargetCardViewModel(s.Id, s.Kind);
                _cards.Add(s.Id, card);
                Cards.Insert(Math.Min(i, Cards.Count), card);
            }
            card.Update(s);
            int at = Cards.IndexOf(card);
            if (at != i && i < Cards.Count) Cards.Move(at, i);

            if (!_history.TryGetValue(s.Id, out ChartHistory? history))
            {
                history = new ChartHistory(ChartSeconds);
                _history.Add(s.Id, history);
            }
            long previous = _lastBytes.GetValueOrDefault(s.Id, s.Bytes);
            history.Add(now, s.Bytes - previous, s.LastResponseMs);
            _lastBytes[s.Id] = s.Bytes;
        }

        foreach (string gone in _cards.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            Cards.Remove(_cards[gone]);
            _cards.Remove(gone);
            _history.Remove(gone);
            _lastBytes.Remove(gone);
            if (FocusedCard?.Id == gone) FocusedCard = null;
        }
    }

    /// <summary>
    /// Worst state among the targets that matter: pinned ones and the machine's own NIC/board, or
    /// every card while nothing is pinned yet. Counts how many are in that state.
    /// </summary>
    private void UpdateOverall()
    {
        List<TargetCardViewModel> watched = Cards.Where(c => c.Pinned || !c.CanPin).ToList();
        if (watched.Count == 0) watched = Cards.ToList();
        if (watched.Count == 0)
        {
            OverallLevel = HealthLevel.Idle;
            OverallText = "감시 대상 없음";
            return;
        }

        HealthLevel worst = watched.Max(c => c.Level);
        int count = watched.Count(c => c.Level == worst);
        OverallLevel = worst;
        OverallText = worst switch
        {
            HealthLevel.Bad => $"이상 {count}",
            HealthLevel.Warn => $"주의 {count}",
            HealthLevel.Ok => "정상",
            _ => "대기 중",
        };
    }

    private void ShowCxp(CxpReport report)
    {
        if (!IsCxpWatching) return;
        CxpMessage = report.Mode switch
        {
            CxpMode.Full => $"{report.BoardName}: 상세 감시 중",
            CxpMode.PresenceOnly => report.Message ?? string.Empty,
            _ => "CXP 보드가 보이지 않음",
        };
        CxpConnections.Clear();
        foreach (CxpConnectionStatus c in report.Connections) CxpConnections.Add(c);
    }

    private void SaveSettings()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CaptureError = "설정 저장 실패: " + ex.Message;
        }
    }
}
