using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSupport.Features;
using VisionSupport.Launcher;
using VisionSupport.Shell;
using VisionSupport.Windows;

namespace VisionSupport.Overview;

/// <summary>
/// The small dialog that answers "what is this thing costing me right now, and what is still
/// running?" - the part of the old home screen worth keeping, at a tenth of the screen area.
///
/// The timer only ticks while the dialog is open (Activate/Deactivate come from the view's
/// Loaded/Closed). A support tool that burns a wakeup every second while nobody is looking at it
/// is exactly the thing this tool exists to catch.
/// </summary>
public sealed partial class OverviewViewModel : ObservableObject
{
    private readonly Process _self = Process.GetCurrentProcess();
    private readonly DateTime _startedAt;
    private readonly DispatcherTimer _timer;
    private readonly LauncherSettings _settings;

    private TimeSpan _lastCpuTotal;
    private DateTime _lastCpuAt;

    [ObservableProperty]
    private string _uptime = string.Empty;

    [ObservableProperty]
    private string _selfMemory = string.Empty;

    [ObservableProperty]
    private string _selfCpu = string.Empty;

    [ObservableProperty]
    private int _threadCount;

    public OverviewViewModel(IReadOnlyList<IFeatureModule> modules, ActivityLog activity,
                             LauncherSettings settings, DateTime startedAt,
                             Action<IFeatureModule> open)
    {
        _settings = settings;
        _startedAt = startedAt;
        Activity = activity;

        foreach (IFeatureModule module in modules) Rows.Add(new FeatureRow(module, open));

        _lastCpuTotal = _self.TotalProcessorTime;
        _lastCpuAt = DateTime.UtcNow;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<FeatureRow> Rows { get; } = new();

    public ActivityLog Activity { get; }

    /// <summary>Bound to the slider. Setting it repaints every open window immediately.</summary>
    public double Opacity
    {
        get => _settings.Opacity;
        set
        {
            double clamped = LauncherSettings.ClampOpacity(value);
            if (Math.Abs(_settings.Opacity - clamped) < 0.001) return;

            _settings.Opacity = clamped;
            WindowEffects.ApplyAlphaToAll(clamped);
            OnPropertyChanged();
        }
    }

    public double MinOpacity => LauncherSettings.MinOpacity;

    public double MaxOpacity => LauncherSettings.MaxOpacity;

    public void Activate() => _timer.Start();

    public void Deactivate()
    {
        _timer.Stop();
        _settings.Save(LauncherSettings.DefaultPath);
    }

    private void Refresh()
    {
        TimeSpan up = DateTime.Now - _startedAt;
        Uptime = up.Days > 0
            ? $"{up.Days}일 {up.Hours}시간 {up.Minutes}분"
            : $"{up.Hours:00}:{up.Minutes:00}:{up.Seconds:00}";

        _self.Refresh();
        SelfMemory = FormatBytes(_self.PrivateMemorySize64);
        ThreadCount = _self.Threads.Count;

        // CPU as a share of one core-second per wall-clock second, divided across all cores, so
        // the number means the same thing as Task Manager's column.
        TimeSpan cpuTotal = _self.TotalProcessorTime;
        DateTime cpuAt = DateTime.UtcNow;
        double elapsed = (cpuAt - _lastCpuAt).TotalSeconds;
        if (elapsed > 0)
        {
            double used = (cpuTotal - _lastCpuTotal).TotalSeconds;
            SelfCpu = (used / (elapsed * Environment.ProcessorCount) * 100.0).ToString("F1") + " %";
        }
        _lastCpuTotal = cpuTotal;
        _lastCpuAt = cpuAt;

        foreach (FeatureRow row in Rows) row.Refresh();
    }

    private static string FormatBytes(long bytes)
    {
        double value = bytes;
        string[] units = { "B", "KB", "MB", "GB" };
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(unit == 0 ? "F0" : "F1") + " " + units[unit];
    }
}

/// <summary>
/// One feature's row. Carries the stop button, which is the only way to bring down a feature that
/// is running with its window closed.
/// </summary>
public sealed partial class FeatureRow : ObservableObject
{
    private readonly Action<IFeatureModule> _open;

    public FeatureRow(IFeatureModule module, Action<IFeatureModule> open)
    {
        Module = module;
        _open = open;
        module.Changed += (_, _) => Refresh();
    }

    public IFeatureModule Module { get; }

    public string Title => Module.Title;

    public FeatureState State => Module.State;

    public string StatusLine => Module.StatusLine;

    public string ToggleLabel => Module.State is FeatureState.Running or FeatureState.Paused
        ? "정지"
        : "실행";

    [RelayCommand]
    private void Open() => _open(Module);

    [RelayCommand]
    private async Task Toggle()
    {
        if (Module.State is FeatureState.Running or FeatureState.Paused) await Module.StopAsync();
        else await Module.StartAsync();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(ToggleLabel));
    }
}
