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
    /// <summary>The round-square button itself.</summary>
    public const double TileSize = 56;

    /// <summary>The tile plus the label under it. Wider than the tile, so the label can breathe.</summary>
    public const double ItemWidth = 88;

    public const double MenuRadius = 112;

    /// <summary>Big enough that a tile at the far edge of the circle, label and all, still fits.</summary>
    public const double ExpandedSize = 360;

    private readonly IReadOnlyList<IFeatureModule> _modules;
    private readonly ActivityLog _activity;
    private readonly LauncherSettings _settings;
    private readonly DateTime _startedAt;
    private readonly Dictionary<IFeatureModule, FeatureWindow> _open = new();

    private OverviewDialog? _overview;
    private LinksDialog? _links;

    [ObservableProperty]
    private bool _isExpanded;

    public LauncherViewModel(IReadOnlyList<IFeatureModule> modules, ActivityLog activity,
                             LauncherSettings settings, LauncherAppearance appearance,
                             DateTime startedAt)
    {
        _modules = modules;
        _activity = activity;
        _settings = settings;
        _startedAt = startedAt;
        Appearance = appearance;

        foreach (IFeatureModule module in modules)
        {
            module.Changed += OnModuleChanged;
            _activity.Add(module.Title, "준비됨");
        }

        Links = new ObservableCollection<LauncherLink>(settings.Links ?? LauncherLink.Defaults());
        RebuildItems();
    }

    /// <summary>
    /// Lays the tiles out around the circle. Called again whenever the user adds or removes a
    /// link: the ring divides by however many items there are, so every tile moves when the
    /// count changes.
    /// </summary>
    public void RebuildItems()
    {
        Items.Clear();

        Point[] offsets = RadialLayout.Offsets(_modules.Count + Links.Count + 1, MenuRadius);

        // The tile sits on the circle; the label hangs below it. So the item is centred
        // horizontally on its own width but vertically on the tile's, not the item's.
        double centreX = ExpandedSize / 2 - ItemWidth / 2;
        double centreY = ExpandedSize / 2 - TileSize / 2;

        int slot = 0;

        foreach (IFeatureModule module in _modules)
        {
            Point at = offsets[slot++];
            Items.Add(new LauncherItem(module.Glyph, module.Title,
                centreX + at.X, centreY + at.Y, () => Open(module), module));
        }

        foreach (LauncherLink link in Links)
        {
            LauncherLink captured = link;
            Point at = offsets[slot++];
            Items.Add(new LauncherItem(captured.Glyph, captured.Title,
                centreX + at.X, centreY + at.Y, () => OpenLink(captured)));
        }

        Point last = offsets[slot];
        Items.Add(new LauncherItem("ViewDashboardOutline", "전체보기",
            centreX + last.X, centreY + last.Y, ShowOverview));
    }

    public ObservableCollection<LauncherItem> Items { get; } = new();

    /// <summary>The user's web tiles, live. The links dialog edits this collection directly.</summary>
    public ObservableCollection<LauncherLink> Links { get; }

    /// <summary>The icon's colour, opacity and size. Shared with the overview dialog, which is
    /// where they are changed.</summary>
    public LauncherAppearance Appearance { get; }

    /// <summary>
    /// Asks the menu to shut instantly rather than animate out. Raised just before something
    /// expensive is built on the UI thread, which an in-flight animation would stutter through.
    /// </summary>
    public event EventHandler? CollapseNowRequested;

    /// <summary>Drives the ring around the icon: something is up even with every window closed.</summary>
    public bool AnyRunning => _modules.Any(m => m.State is FeatureState.Running
                                                       or FeatureState.Starting
                                                       or FeatureState.Paused);

    [RelayCommand]
    public void ShowOverview()
    {
        CollapseNowRequested?.Invoke(this, EventArgs.Empty);

        if (_overview is not null)
        {
            _overview.Activate();
            return;
        }

        var viewModel = new OverviewViewModel(_modules, _activity, Appearance, _startedAt, Open);
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

    /// <summary>
    /// Opens the links editor. Closing it rebuilds the ring, because adding or removing a tile
    /// moves every other tile.
    /// </summary>
    [RelayCommand]
    public void ShowLinks()
    {
        CollapseNowRequested?.Invoke(this, EventArgs.Empty);

        if (_links is not null)
        {
            _links.Activate();
            return;
        }

        _links = new LinksDialog(new LinksViewModel(Links));
        _links.Closed += (_, _) =>
        {
            _links = null;
            _settings.Links = Links.ToList();
            _settings.Save(LauncherSettings.DefaultPath);
            RebuildItems();
        };
        _links.Show();
    }

    private void OpenLink(LauncherLink link)
    {
        CollapseNowRequested?.Invoke(this, EventArgs.Empty);

        if (string.IsNullOrWhiteSpace(link.Url))
        {
            _activity.Add(link.Title, "주소가 비어 있습니다");
            return;
        }

        BrowserLauncher.OpenPopup(link.Url, link.Width, link.Height);
        _activity.Add(link.Title, "열기");
    }

    private void Open(IFeatureModule module)
    {
        CollapseNowRequested?.Invoke(this, EventArgs.Empty);

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
