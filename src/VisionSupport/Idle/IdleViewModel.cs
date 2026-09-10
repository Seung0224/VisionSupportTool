using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSupport.Features;
using VisionSupport.Shell;

namespace VisionSupport.Idle;

/// <summary>
/// The home screen of a tool that is left running all day: what is up, what it is doing, and
/// what this process itself is costing the machine.
///
/// The timer only ticks while the page is on screen (<see cref="Activate"/> / <see cref="Deactivate"/>
/// are called from the view's Loaded/Unloaded). A support tool that burns a wakeup every second
/// while nobody is looking at it is exactly the thing this tool exists to catch.
/// </summary>
public sealed partial class IdleViewModel : ObservableObject
{
    private readonly Process _self = Process.GetCurrentProcess();
    private readonly DateTime _startedAt = DateTime.Now;
    private readonly DispatcherTimer _timer;
    private readonly Action<IFeatureModule> _navigate;

    private TimeSpan _lastCpuTotal;
    private DateTime _lastCpuAt;

    [ObservableProperty]
    private string _clock = string.Empty;

    [ObservableProperty]
    private string _uptime = string.Empty;

    [ObservableProperty]
    private string _selfMemory = string.Empty;

    [ObservableProperty]
    private string _selfCpu = string.Empty;

    [ObservableProperty]
    private int _threadCount;

    public IdleViewModel(IReadOnlyList<IFeatureModule> modules, ActivityLog activity,
                         Action<IFeatureModule> navigate)
    {
        _navigate = navigate;
        Activity = activity;

        foreach (IFeatureModule module in modules) Cards.Add(new FeatureCard(module));

        _lastCpuTotal = _self.TotalProcessorTime;
        _lastCpuAt = DateTime.UtcNow;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<FeatureCard> Cards { get; } = new();

    public ActivityLog Activity { get; }

    public bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);

    public string PrivilegeLabel => IsElevated ? "관리자 권한" : "일반 권한 (기능 제한)";

    public void Activate() => _timer.Start();

    public void Deactivate() => _timer.Stop();

    [RelayCommand]
    private void Open(FeatureCard card) => _navigate(card.Module);

    [RelayCommand]
    private async Task Toggle(FeatureCard card)
    {
        if (card.Module.State is FeatureState.Running) await card.Module.PauseAsync();
        else await card.Module.StartAsync();
    }

    private void Refresh()
    {
        DateTime now = DateTime.Now;
        Clock = now.ToString("yyyy-MM-dd  HH:mm:ss");

        TimeSpan up = now - _startedAt;
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
            double percent = used / (elapsed * Environment.ProcessorCount) * 100.0;
            SelfCpu = percent.ToString("F1") + " %";
        }
        _lastCpuTotal = cpuTotal;
        _lastCpuAt = cpuAt;

        foreach (FeatureCard card in Cards) card.Refresh();
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

/// <summary>One feature's tile on the idle screen.</summary>
public sealed partial class FeatureCard : ObservableObject
{
    public FeatureCard(IFeatureModule module)
    {
        Module = module;
        module.Changed += (_, _) => Refresh();
    }

    public IFeatureModule Module { get; }

    public string Title => Module.Title;

    public string Description => Module.Description;

    public FeatureState State => Module.State;

    public string StatusLine => Module.StatusLine;

    public string ToggleLabel => Module.State == FeatureState.Running ? "정지" : "실행";

    public void Refresh()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(ToggleLabel));
    }
}
