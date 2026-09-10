using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSupport.Features;
using VisionSupport.Overview;
using VisionSupport.Shell;
using VisionSupport.Windows;

namespace VisionSupport.Launcher;

/// <summary>
/// What the floating icon opens, and what it remembers about what is already open.
///
/// One window per feature at most: clicking a feature whose window is up brings that window
/// forward rather than building a second one, because two views over one ViewModel would fight
/// over the same chart and the same node table.
/// </summary>
public sealed partial class LauncherViewModel : ObservableObject
{
    /// <summary>Tile size and menu radius. The expanded window is sized to fit these.</summary>
    public const double TileSize = 56;

    public const double MenuRadius = 110;

    public const double ExpandedSize = 340;

    private readonly IReadOnlyList<IFeatureModule> _modules;
    private readonly ActivityLog _activity;
    private readonly LauncherSettings _settings;
    private readonly DateTime _startedAt;
    private readonly Dictionary<IFeatureModule, FeatureWindow> _open = new();

    private OverviewDialog? _overview;

    [ObservableProperty]
    private bool _isExpanded;

    public LauncherViewModel(IReadOnlyList<IFeatureModule> modules, ActivityLog activity,
                             LauncherSettings settings, DateTime startedAt)
    {
        _modules = modules;
        _activity = activity;
        _settings = settings;
        _startedAt = startedAt;

        Point[] offsets = RadialLayout.Offsets(modules.Count + 1, MenuRadius);
        double centre = ExpandedSize / 2 - TileSize / 2;

        for (int i = 0; i < modules.Count; i++)
        {
            IFeatureModule module = modules[i];
            Items.Add(new LauncherItem(module.Glyph, module.Title,
                centre + offsets[i].X, centre + offsets[i].Y,
                () => Open(module), module));

            module.Changed += OnModuleChanged;
            _activity.Add(module.Title, "준비됨");
        }

        Point last = offsets[^1];
        Items.Add(new LauncherItem("", "전체보기",
            centre + last.X, centre + last.Y, ShowOverview));
    }

    public ObservableCollection<LauncherItem> Items { get; } = new();

    /// <summary>Drives the ring around the icon: something is up even with every window closed.</summary>
    public bool AnyRunning => _modules.Any(m => m.State is FeatureState.Running
                                                       or FeatureState.Starting
                                                       or FeatureState.Paused);

    [RelayCommand]
    public void ShowOverview()
    {
        IsExpanded = false;

        if (_overview is not null)
        {
            _overview.Activate();
            return;
        }

        var viewModel = new OverviewViewModel(_modules, _activity, _settings, _startedAt, Open);
        _overview = new OverviewDialog(viewModel);
        _overview.Closed += (_, _) => _overview = null;
        _overview.Show();
    }

    /// <summary>
    /// Stops every feature and ends the process. Windows are force-closed first: their normal
    /// close path would run the stop-or-release decision again and race this teardown.
    /// </summary>
    public async Task ExitAsync()
    {
        foreach (FeatureWindow window in _open.Values.ToArray()) window.ForceClose();
        _open.Clear();

        _overview?.Close();

        // Sequential, not parallel: teardown touches ETW sessions and sockets, and a failure in
        // one should not be racing another's.
        foreach (IFeatureModule module in _modules) await module.StopAsync();

        _settings.Save(LauncherSettings.DefaultPath);
        Application.Current.Shutdown();
    }

    private void Open(IFeatureModule module)
    {
        IsExpanded = false;

        if (_open.TryGetValue(module, out FeatureWindow? existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        var window = new FeatureWindow(module);
        window.Closed += (_, _) => _open.Remove(module);
        _open[module] = window;
        window.Show();
    }

    private void OnModuleChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AnyRunning));

        if (sender is FeatureModule { State: FeatureState.Faulted } faulted)
        {
            _activity.Add(faulted.Title, "오류: " + (faulted.FaultMessage ?? "알 수 없음"));
        }
    }
}
