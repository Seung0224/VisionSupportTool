using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemMon.Models;
using MemMon.Services;
using Microsoft.Win32;

namespace MemMon.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// 12 hours of 1 Hz samples. The chart draws only its current time window (default 30 min),
    /// so this bounds how far back the user can zoom out and how much the buffer holds
    /// (~2 MB of <see cref="MemorySample"/> records), not the cost of a redraw.
    /// </summary>
    private const int MaxSamples = 43_200;

    /// <summary>A busy program collects often; keep the most recent window rather than everything.</summary>
    private const int MaxGcEvents = 1000;

    /// <summary>Unusual collections are rare and are the whole point, so they get their own budget.</summary>
    private const int MaxUnusualGcEvents = 500;

    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly List<MemorySample> _samples = new();
    private readonly List<(double Seconds, string Label)> _snapshotMarkers = new();
    private readonly int[] _collectionsByGeneration = new int[3];
    private TargetSession? _session;
    private GcEtwMonitor? _gcMonitor;
    private NativeAllocMonitor? _nativeMonitor;
    private string? _nativeStartError;
    private CancellationTokenSource? _snapshotCts;
    private int _snapshotCounter;
    private double _maxPauseMs;
    private double _totalPauseMs;
    private long _finalizationPromotedTotal;

    public IReadOnlyList<MemorySample> Samples => _samples;

    /// <summary>Where on the timeline each snapshot was taken, so the chart can mark them.</summary>
    public IReadOnlyList<(double Seconds, string Label)> SnapshotMarkers => _snapshotMarkers;
    public ObservableCollection<TargetProcessInfo> Processes { get; } = new();
    public ObservableCollection<HeapSnapshot> Snapshots { get; } = new();
    public ObservableCollection<TypeStat> TypeRows { get; } = new();
    public ObservableCollection<DiffRow> DiffRows { get; } = new();
    public ObservableCollection<GcEvent> GcEvents { get; } = new();

    /// <summary>
    /// Native memory by the module that asked for it. This is the only view that can explain
    /// growth living outside the managed heap, which the type tables cannot see at all.
    /// </summary>
    public ObservableCollection<NativeModuleStat> NativeModules { get; } = new();

    /// <summary>
    /// Collections that departed from the routine baseline. Kept separately so the flood of
    /// ordinary gen0 collections cannot push the one that stalled the program out of view.
    /// </summary>
    public ObservableCollection<GcEvent> UnusualGcEvents { get; } = new();

    /// <summary>ETW real-time sessions need elevation; without it the GC tab explains why.</summary>
    public bool IsElevated => GcEtwMonitor.IsElevated;

    /// <summary>Raised after each sample so the view can redraw the chart.</summary>
    public event EventHandler? SamplesChanged;

    [ObservableProperty] private TargetProcessInfo? _selectedProcess;
    [ObservableProperty] private HeapSnapshot? _selectedSnapshot;
    [ObservableProperty] private HeapSnapshot? _diffBefore;
    [ObservableProperty] private HeapSnapshot? _diffAfter;
    [ObservableProperty] private string _typeFilter = "";
    [ObservableProperty] private string _diffFilter = "";
    [ObservableProperty] private string _status = "감시할 프로세스를 선택하세요.";

    // Split per metric rather than one formatted line, so each term can carry its own tooltip.
    [ObservableProperty] private string _liveManagedTotal = "";
    [ObservableProperty] private string _liveGen0 = "";
    [ObservableProperty] private string _liveGen1 = "";
    [ObservableProperty] private string _liveGen2 = "";
    [ObservableProperty] private string _liveLoh = "";
    [ObservableProperty] private string _livePrivate = "";
    [ObservableProperty] private string _snapshotSummary = "";
    [ObservableProperty] private string _comparisonSummary = "";
    [ObservableProperty] private string _gcSummary = "";
    [ObservableProperty] private string _nativeSummary = "";

    /// <summary>Why the native table is empty, when it is. Never overwritten by a rebuild.</summary>
    [ObservableProperty] private string _nativeStatus = "";
    [ObservableProperty] private string _etwStatus = "";
    [ObservableProperty] private bool _isAttached;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Drives the spinner: the scan takes seconds and must not freeze the window.</summary>
    [ObservableProperty] private bool _isScanning;

    public MainViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await SampleTickAsync();
        // So the native tab explains itself before anything has been attached to.
        RefreshNativeStatus();
        _ = RefreshProcessesAsync();
    }

    // ---- target selection ------------------------------------------------

    /// <summary>
    /// Walking every process's module list takes seconds, so it happens off the UI thread and
    /// the window stays responsive while the spinner runs.
    /// </summary>
    [RelayCommand]
    private async Task RefreshProcessesAsync()
    {
        if (IsScanning) return;

        int? keep = SelectedProcess?.Pid;
        IsScanning = true;
        Status = "프로세스를 찾는 중...";
        try
        {
            List<TargetProcessInfo> scanned = await Task.Run(ProcessScanner.Scan);

            Processes.Clear();
            foreach (TargetProcessInfo p in scanned) Processes.Add(p);
            SelectedProcess = Processes.FirstOrDefault(p => p.Pid == keep) ?? Processes.FirstOrDefault();
            Status = $".NET 프로세스 {Processes.Count}개를 찾았습니다.";
        }
        catch (Exception ex)
        {
            Status = $"프로세스 목록을 만들지 못했습니다: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void Attach()
    {
        if (SelectedProcess is null) return;
        if (SelectedProcess.Is32Bit)
        {
            Status = "32비트 프로세스입니다. 64비트 MemMon으로는 attach할 수 없습니다.";
            return;
        }
        if (SelectedProcess.NeedsElevation)
        {
            Status = $"{SelectedProcess.Name}은(는) 관리자 권한으로 실행 중이라 지금은 읽을 수 없습니다. " +
                     "MemMon도 관리자 권한으로 다시 실행하세요.";
            return;
        }

        Detach();
        try
        {
            _session = TargetSession.Attach(SelectedProcess.Pid);
            _samples.Clear();
            _snapshotMarkers.Clear();
            IsAttached = true;
            _timer.Start();
            StartGcMonitor(SelectedProcess.Pid);
            StartNativeMonitor(SelectedProcess.Pid);
            Status = $"{SelectedProcess.Name} ({SelectedProcess.Pid}) 감시 중 — {_session.ClrDescription}";
        }
        catch (Exception ex)
        {
            Status = $"attach 실패: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Detach()
    {
        _timer.Stop();
        _snapshotCts?.Cancel();
        StopGcMonitor();
        StopNativeMonitor();
        _session?.Dispose();
        _session = null;
        IsAttached = false;
        LiveManagedTotal = LiveGen0 = LiveGen1 = LiveGen2 = LiveLoh = LivePrivate = "";
    }

    /// <summary>Starts an executable and attaches to it once its CLR is up.</summary>
    [RelayCommand]
    private async Task LaunchAndAttachAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "감시할 실행 파일 선택",
            Filter = "실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var startInfo = new ProcessStartInfo(dialog.FileName)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(dialog.FileName) ?? "",
            };
            using Process? started = Process.Start(startInfo);
            if (started is null)
            {
                Status = "프로세스를 시작하지 못했습니다.";
                return;
            }

            Status = "프로세스를 시작했습니다. CLR이 로드되기를 기다리는 중...";
            int pid = started.Id;

            // The CLR is not loaded at CreateProcess time; poll briefly until ClrMD can see it.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(500);
                if (started.HasExited)
                {
                    Status = "대상 프로세스가 즉시 종료되었습니다.";
                    return;
                }

                try
                {
                    Detach();
                    _session = TargetSession.Attach(pid);
                    _samples.Clear();
                    _snapshotMarkers.Clear();
                    IsAttached = true;
                    _timer.Start();
                    StartGcMonitor(pid);
                    StartNativeMonitor(pid);
                    await RefreshProcessesAsync();
                    SelectedProcess = Processes.FirstOrDefault(p => p.Pid == pid);
                    Status = $"{started.ProcessName} ({pid}) 감시 중 — {_session.ClrDescription}";
                    return;
                }
                catch
                {
                    // CLR not up yet; try again.
                }
            }

            Status = "10초 안에 CLR을 찾지 못했습니다. 관리되지 않는 프로세스일 수 있습니다.";
        }
        catch (Exception ex)
        {
            Status = $"실행 실패: {ex.Message}";
        }
    }

    // ---- live sampling ---------------------------------------------------

    private async Task SampleTickAsync()
    {
        if (_session is null || IsBusy) return;

        try
        {
            MemorySample sample = await _session.SampleAsync(CancellationToken.None);
            _samples.Add(sample);
            if (_samples.Count > MaxSamples)
            {
                _samples.RemoveRange(0, _samples.Count - MaxSamples);
                // Drop snapshot markers that fell off the back of the buffer: a marker with no
                // samples left beside it has nothing to point at.
                double oldest = _samples[0].ElapsedSeconds;
                _snapshotMarkers.RemoveAll(m => m.Seconds < oldest);
            }

            LiveManagedTotal = ByteSize.Format(sample.ManagedTotalBytes);
            LiveGen0 = ByteSize.Format(sample.Gen0Bytes);
            LiveGen1 = ByteSize.Format(sample.Gen1Bytes);
            LiveGen2 = ByteSize.Format(sample.Gen2Bytes);
            LiveLoh = ByteSize.Format(sample.LohBytes);
            LivePrivate = ByteSize.Format((ulong)Math.Max(0, sample.PrivateBytes));

            SamplesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _timer.Stop();
            IsAttached = false;
            Status = $"감시 중단 — 대상 프로세스를 읽을 수 없습니다: {ex.Message}";
        }
    }

    // ---- GC events (ETW) -------------------------------------------------

    private void StartGcMonitor(int pid)
    {
        GcEvents.Clear();
        UnusualGcEvents.Clear();
        Array.Clear(_collectionsByGeneration);
        _maxPauseMs = 0;
        _totalPauseMs = 0;
        _finalizationPromotedTotal = 0;
        GcSummary = "";

        if (!GcEtwMonitor.IsElevated)
        {
            EtwStatus = "관리자 권한이 아니라 GC 일시정지 시간은 측정하지 않습니다.";
            return;
        }

        try
        {
            _gcMonitor = GcEtwMonitor.Start(pid);
            _gcMonitor.GcObserved += OnGcObserved;
            EtwStatus = "GC 이벤트 수집 중";
        }
        catch (Exception ex)
        {
            EtwStatus = $"ETW 세션을 열지 못했습니다: {ex.Message}";
        }
    }

    private void StopGcMonitor()
    {
        if (_gcMonitor is null) return;
        _gcMonitor.GcObserved -= OnGcObserved;
        _gcMonitor.Dispose();
        _gcMonitor = null;
        EtwStatus = "";
    }

    /// <summary>Events arrive on the ETW pump thread; the collections belong to the UI thread.</summary>
    private void OnGcObserved(object? sender, GcEvent collected)
        => _dispatcher.BeginInvoke(() => RecordGcEvent(collected));

    private void RecordGcEvent(GcEvent collected)
    {
        GcEvents.Insert(0, collected);
        if (GcEvents.Count > MaxGcEvents) GcEvents.RemoveAt(GcEvents.Count - 1);

        if (GcAnomaly.IsUnusual(collected))
        {
            UnusualGcEvents.Insert(0, collected);
            if (UnusualGcEvents.Count > MaxUnusualGcEvents)
                UnusualGcEvents.RemoveAt(UnusualGcEvents.Count - 1);
        }

        if (collected.Generation is >= 0 and < 3) _collectionsByGeneration[collected.Generation]++;
        _maxPauseMs = Math.Max(_maxPauseMs, collected.PauseMs);
        _totalPauseMs += collected.PauseMs;
        _finalizationPromotedTotal += collected.FinalizationPromotedCount;

        GcSummary =
            $"Gen0 {_collectionsByGeneration[0]:N0}회   " +
            $"Gen1 {_collectionsByGeneration[1]:N0}회   " +
            $"Gen2 {_collectionsByGeneration[2]:N0}회   " +
            $"최대 일시정지 {_maxPauseMs:N1} ms   " +
            $"총 일시정지 {_totalPauseMs / 1000.0:N2} 초   " +
            $"파이널라이즈 승격 {_finalizationPromotedTotal:N0}개   " +
            $"특이 {UnusualGcEvents.Count:N0}건";
    }

    /// <summary>Relaunches elevated so the ETW session can be created.</summary>
    [RelayCommand]
    private void RestartAsAdmin()
    {
        string? exePath = Environment.ProcessPath;
        if (exePath is null) return;

        try
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Status = $"관리자 권한으로 다시 실행하지 못했습니다: {ex.Message}";
        }
    }

    // ---- native allocations (kernel ETW) ---------------------------------

    private void StartNativeMonitor(int pid)
    {
        NativeModules.Clear();
        NativeSummary = "";
        _nativeStartError = null;

        if (!NativeAllocMonitor.IsElevated)
        {
            _nativeStartError = "관리자 권한이 아닙니다.";
            RefreshNativeStatus();
            return;
        }

        try
        {
            _nativeMonitor = NativeAllocMonitor.Start(pid);
        }
        catch (Exception ex)
        {
            // Kernel ETW sessions are a limited system resource; other tools (Visual Studio's
            // diagnostics hub, profilers) can exhaust them. Say so instead of showing an empty
            // table that looks like "nothing is leaking".
            _nativeStartError = ex.Message;
            Status = $"네이티브 추적을 시작하지 못했습니다: {ex.Message}";
        }
        RefreshNativeStatus();
    }

    private void StopNativeMonitor()
    {
        _nativeMonitor?.Dispose();
        _nativeMonitor = null;
    }

    private void RefreshNativeStatus()
    {
        bool hasSnapshots = DiffBefore is not null && DiffAfter is not null;
        bool carriesNative = hasSnapshots
            && (DiffBefore!.NativeModules.Count > 0 || DiffAfter!.NativeModules.Count > 0);
        NativeStatus = NativeTrackingMessage.Explain(
            _nativeStartError, hasSnapshots, carriesNative, NativeModules.Count);
    }

    /// <summary>
    /// Rebuilds the native table from the two snapshots being compared, so it answers the same
    /// question as the type table: what happened between these two moments.
    /// </summary>
    private void RebuildNativeModules()
    {
        NativeModules.Clear();

        if (DiffBefore is null || DiffAfter is null)
        {
            NativeSummary = "";
            RefreshNativeStatus();
            return;
        }

        IReadOnlyList<NativeModuleStat> rows = NativeModuleDiffer.Diff(DiffBefore, DiffAfter);
        foreach (NativeModuleStat row in rows) NativeModules.Add(row);

        long allocated = rows.Sum(r => r.AllocatedBytes);
        long freed = rows.Sum(r => r.FreedBytes);
        NativeSummary = rows.Count == 0
            ? ""
            : $"A → B 네이티브 순증 {ByteSize.FormatSigned(allocated - freed)}   " +
              $"할당 {ByteSize.Format((ulong)Math.Max(0, allocated))}   " +
              $"해제 {ByteSize.Format((ulong)Math.Max(0, freed))}   " +
              $"모듈 {rows.Count:N0}개";

        RefreshNativeStatus();
    }

    // ---- snapshots -------------------------------------------------------

    [RelayCommand]
    private async Task TakeSnapshotAsync()
    {
        if (_session is null || IsBusy) return;

        IsBusy = true;
        Status = "스냅샷 수집 중 — 대상이 잠시 멈춥니다...";
        _snapshotCts = new CancellationTokenSource();
        try
        {
            // Taken before the walk, so the mark sits where the snapshot started rather than
            // where it finished - a big heap can take seconds to walk.
            double markerSeconds = _samples.Count > 0 ? _samples[^1].ElapsedSeconds : 0;
            string label = $"#{++_snapshotCounter}  {DateTime.Now:HH:mm:ss}";
            IReadOnlyList<NativeModuleStat> native =
                _nativeMonitor?.GetStats() ?? Array.Empty<NativeModuleStat>();
            HeapSnapshot snapshot = await _session.TakeSnapshotAsync(label, native, _snapshotCts.Token);
            _snapshotMarkers.Add((markerSeconds, $"#{_snapshotCounter}"));
            SamplesChanged?.Invoke(this, EventArgs.Empty);

            Snapshots.Add(snapshot);
            SelectedSnapshot = snapshot;
            DiffBefore ??= snapshot;
            DiffAfter = snapshot;
            Status = $"스냅샷 완료 — {snapshot.TotalObjects:N0}개 객체, " +
                     $"{ByteSize.Format(snapshot.TotalBytes)}, {snapshot.WalkSeconds:F1}초 소요";
        }
        catch (OperationCanceledException)
        {
            Status = "스냅샷을 취소했습니다.";
        }
        catch (Exception ex)
        {
            Status = $"스냅샷 실패: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _snapshotCts?.Dispose();
            _snapshotCts = null;
        }
    }

    [RelayCommand]
    private void SaveSnapshot()
    {
        if (SelectedSnapshot is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "스냅샷 저장",
            Filter = "MemMon 스냅샷 (*.memsnap.json)|*.memsnap.json",
            FileName = $"{SelectedSnapshot.ProcessName}-{SelectedSnapshot.TakenAt:yyyyMMdd-HHmmss}.memsnap.json",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            SnapshotStore.Save(SelectedSnapshot, dialog.FileName);
            Status = $"저장했습니다: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            Status = $"저장 실패: {ex.Message}";
        }
    }

    [RelayCommand]
    private void LoadSnapshot()
    {
        var dialog = new OpenFileDialog
        {
            Title = "스냅샷 불러오기",
            Filter = "MemMon 스냅샷 (*.memsnap.json)|*.memsnap.json|모든 파일 (*.*)|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;

        foreach (string file in dialog.FileNames)
        {
            try
            {
                HeapSnapshot loaded = SnapshotStore.Load(file);
                Snapshots.Add(loaded);
                SelectedSnapshot = loaded;
            }
            catch (Exception ex)
            {
                Status = $"불러오기 실패 ({Path.GetFileName(file)}): {ex.Message}";
                return;
            }
        }
        Status = $"스냅샷 {dialog.FileNames.Length}개를 불러왔습니다.";
    }

    [RelayCommand]
    private void ClearSnapshots()
    {
        Snapshots.Clear();
        _snapshotMarkers.Clear();
        SelectedSnapshot = null;
        DiffBefore = null;
        DiffAfter = null;
        TypeRows.Clear();
        DiffRows.Clear();
        _snapshotCounter = 0;
    }

    // ---- derived tables --------------------------------------------------

    partial void OnSelectedSnapshotChanged(HeapSnapshot? value) => RebuildTypeRows();

    partial void OnTypeFilterChanged(string value) => RebuildTypeRows();

    partial void OnDiffBeforeChanged(HeapSnapshot? value) => RebuildDiffRows();

    partial void OnDiffAfterChanged(HeapSnapshot? value) => RebuildDiffRows();

    partial void OnDiffFilterChanged(string value) => RebuildDiffRows();

    private void RebuildTypeRows()
    {
        TypeRows.Clear();
        if (SelectedSnapshot is null)
        {
            SnapshotSummary = "";
            return;
        }

        foreach (TypeStat row in Filter(SelectedSnapshot.Types, TypeFilter, t => t.TypeName))
            TypeRows.Add(row);

        SnapshotSummary =
            $"{SelectedSnapshot.TakenAt:yyyy-MM-dd HH:mm:ss}   " +
            $"객체 {SelectedSnapshot.TotalObjects:N0}개   " +
            $"관리 힙 {ByteSize.Format(SelectedSnapshot.TotalBytes)}   " +
            $"빈 공간 {ByteSize.Format(SelectedSnapshot.FreeBytes)}   " +
            $"타입 {SelectedSnapshot.Types.Count:N0}종   " +
            $"Private {ByteSize.Format((ulong)Math.Max(0, SelectedSnapshot.PrivateBytes))}   " +
            $"수집 {SelectedSnapshot.WalkSeconds:F1}초";
    }

    private void RebuildDiffRows()
    {
        DiffRows.Clear();
        if (DiffBefore is null || DiffAfter is null)
        {
            ComparisonSummary = "";
            RebuildNativeModules();
            return;
        }

        RebuildNativeModules();

        MemoryDelta delta = SnapshotComparison.Compare(DiffBefore, DiffAfter);
        string share = delta.NativeShare > 0
            ? $"  ({delta.NativeShare:P1}가 관리 힙 밖)"
            : "";
        ComparisonSummary =
            $"프로세스 전체 {ByteSize.FormatSigned(delta.PrivateBytes)}   =   " +
            $"관리 힙 {ByteSize.FormatSigned(delta.ManagedBytes)}   +   " +
            $"네이티브 {ByteSize.FormatSigned(delta.NativeBytes)}{share}";

        IReadOnlyList<DiffRow> diff = SnapshotDiffer.Diff(DiffBefore, DiffAfter);
        foreach (DiffRow row in Filter(diff, DiffFilter, r => r.TypeName))
            DiffRows.Add(row);
    }

    private static IEnumerable<T> Filter<T>(IEnumerable<T> source, string filter, Func<T, string> key)
    {
        if (string.IsNullOrWhiteSpace(filter)) return source;
        return source.Where(item => key(item).Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() => Detach();
}
