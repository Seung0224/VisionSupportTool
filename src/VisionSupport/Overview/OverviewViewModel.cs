using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSupport.Features;
using VisionSupport.Launcher;
using VisionSupport.Shell;

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
                             LauncherAppearance appearance, DateTime startedAt,
                             Action<IFeatureModule> open)
    {
        _startedAt = startedAt;
        Activity = activity;
        Appearance = appearance;

        foreach (IFeatureModule module in modules) Rows.Add(new FeatureRow(module, open));

        _lastCpuTotal = _self.TotalProcessorTime;
        _lastCpuAt = DateTime.UtcNow;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<FeatureRow> Rows { get; } = new();

    public ActivityLog Activity { get; }

    /// <summary>The launcher icon's colour, opacity and size. Changing one repaints the icon
    /// straight away; this dialog is the only place they can be reached.</summary>
    public LauncherAppearance Appearance { get; }

    public void Activate() => _timer.Start();

    public void Deactivate()
    {
        _timer.Stop();
        foreach (FeatureRow row in Rows) row.Detach();

        // Written once on the way out rather than on every slider tick, which would be a file
        // write per frame of a drag.
        Appearance.Save();
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
/// One feature's row: whether its tool is doing anything, and a way to open it. Starting and
/// stopping belong to each tool's own window.
/// </summary>
public sealed partial class FeatureRow : ObservableObject
{
    private readonly Action<IFeatureModule> _open;

    public FeatureRow(IFeatureModule module, Action<IFeatureModule> open)
    {
        Module = module;
        _open = open;
        module.Changed += OnModuleChanged;
    }

    public IFeatureModule Module { get; }

    public string Title => Module.Title;

    public Brush StateBrush => FeatureBrushes.For(Module);

    public string StatusLine => Module.StatusLine;

    [RelayCommand]
    private void Open() => _open(Module);

    public void Refresh()
    {
        OnPropertyChanged(nameof(StateBrush));
        OnPropertyChanged(nameof(StatusLine));
    }

    /// <summary>Drops the subscription to Module.Changed. Module outlives every dialog open, so
    /// without this each reopen would leave one more row permanently rooted behind it.</summary>
    public void Detach() => Module.Changed -= OnModuleChanged;

    private void OnModuleChanged(object? sender, EventArgs e) => Refresh();
}
