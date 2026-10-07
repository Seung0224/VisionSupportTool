using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSupport.Archive;
using VisionSupport.Features;
using VisionSupport.Overview;
using VisionSupport.Sam;
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
    /// <summary>Every feature, on the ring or in the tools box. Exit and the overview go through
    /// all of them.</summary>
    private readonly IReadOnlyList<IFeatureModule> _modules;

    /// <summary>The features with a tile of their own.</summary>
    private readonly IReadOnlyList<IFeatureModule> _ringModules;

    private readonly ActivityLog _activity;
    private readonly LauncherSettings _settings;
    private readonly DateTime _startedAt;
    private readonly Dictionary<IFeatureModule, FeatureWindow> _open = new();

    private OverviewDialog? _overview;
    private LinksDialog? _links;

    /// <summary>Kept for its running ring, which follows no single feature.</summary>
    private LauncherItem? _toolsItem;

    [ObservableProperty]
    private bool _isExpanded;

    /// <param name="modules">Features with a tile of their own on the ring.</param>
    /// <param name="tools">Features that open from the tools box instead.</param>
    public LauncherViewModel(IReadOnlyList<IFeatureModule> modules, IReadOnlyList<IFeatureModule> tools,
                             ActivityLog activity, LauncherSettings settings,
                             LauncherAppearance appearance, DateTime startedAt)
    {
        // Tools first, so the overview lists them where they used to sit on the ring.
        _modules = tools.Concat(modules).ToList();
        _ringModules = modules;
        _activity = activity;
        _settings = settings;
        _startedAt = startedAt;
        Appearance = appearance;
        Drops = new DropCoordinator(settings, activity);

        foreach (IFeatureModule module in _modules)
        {
            module.Changed += OnModuleChanged;
            _activity.Add(module.Title, "준비됨");
        }

        Tools = tools.Select(tool => new LauncherItem(tool.Glyph, tool.Title, 0, 0, () => Open(tool), tool))
                     .ToList();

        List<LauncherLink> saved = settings.Links ?? LauncherLink.Defaults();
        WebLinks = new ObservableCollection<LauncherLink>(saved.Where(l => l.Kind == LinkKind.Web));
        FolderLinks = new ObservableCollection<LauncherLink>(saved.Where(l => l.Kind == LinkKind.Folder));

        // Rebuild as soon as a link is added or removed, not when the editor is closed. Adding a
        // tile and finding the menu unchanged reads as "it did not work", and the editor is
        // modeless - there is no moment where the user has agreed to be finished.
        WebLinks.CollectionChanged += (_, _) => RebuildItems();
        FolderLinks.CollectionChanged += (_, _) => RebuildItems();

        // Both sizes move the ring: the tile's directly, the icon's through the clearance the
        // ring keeps from it.
        appearance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LauncherAppearance.TileSize)
                              or nameof(LauncherAppearance.IconSize))
            {
                RebuildItems();
            }
        };

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

        // Web links get a tile each; folders share one, because a dozen of them would crowd the
        // ring out of usefulness. The folder tile opens a list of its own instead.
        bool hasFolders = FolderLinks.Count > 0;

        // Always there: the tools tile and the overview (and the cutout tile, while it is enabled).
        Point[] offsets = RadialLayout.Offsets(
            _ringModules.Count + WebLinks.Count + (hasFolders ? 1 : 0) + (CutoutEnabled ? 3 : 2), Appearance.MenuRadius);

        // A tile is the whole item now, so both axes centre on the tile.
        double centre = Appearance.MenuSize / 2 - Appearance.TileSize / 2;
        double centreX = centre;
        double centreY = centre;

        int slot = 0;

        // The tools share one tile and open a box beside the ring, like the folders.
        Point toolsAt = offsets[slot++];
        _toolsItem = new LauncherItem("Toolbox", "도구", centreX + toolsAt.X, centreY + toolsAt.Y,
            () => ToolBoxRequested?.Invoke(this, EventArgs.Empty),
            isRunning: () => Tools.Any(tool => tool.IsRunning));
        Items.Add(_toolsItem);

        foreach (IFeatureModule module in _ringModules)
        {
            Point at = offsets[slot++];
            Items.Add(new LauncherItem(module.Glyph, module.Title,
                centreX + at.X, centreY + at.Y, () => Open(module), module));
        }

        // A mode rather than a window: the screen dims and whatever is under the cursor can be cut out.
        if (CutoutEnabled)
        {
            Point cutoutAt = offsets[slot++];
            Items.Add(new LauncherItem("ContentCut", "누끼", centreX + cutoutAt.X, centreY + cutoutAt.Y, StartCutout));
        }

        foreach (LauncherLink link in WebLinks)
        {
            LauncherLink captured = link;
            Point at = offsets[slot++];
            Items.Add(new LauncherItem(captured.Glyph, captured.Title,
                centreX + at.X, centreY + at.Y, () => OpenWebLink(captured)));
        }

        if (hasFolders)
        {
            Point at = offsets[slot++];
            Items.Add(new LauncherItem("FolderMultipleOutline", "폴더",
                centreX + at.X, centreY + at.Y,
                () => FolderListRequested?.Invoke(this, EventArgs.Empty)));
        }

        Point last = offsets[slot];
        Items.Add(new LauncherItem("ViewDashboardOutline", "전체보기",
            centreX + last.X, centreY + last.Y, ShowOverview));
    }

    public ObservableCollection<LauncherItem> Items { get; } = new();

    /// <summary>Tiles that open a page. One each, on the ring.</summary>
    public ObservableCollection<LauncherLink> WebLinks { get; }

    /// <summary>
    /// Folders. These share a single tile: clicking it lists them beside the menu, and the list
    /// is where one gets picked. A ring with a dozen folders on it would have no room for
    /// anything else.
    /// </summary>
    public ObservableCollection<LauncherLink> FolderLinks { get; }

    /// <summary>Raised when the folder tile is pressed, for the window to show the list.</summary>
    public event EventHandler? FolderListRequested;

    /// <summary>
    /// The tools box: features that open from a small grid beside the ring rather than from a
    /// tile of their own. Built once - nothing about the box moves when links are added.
    /// </summary>
    public IReadOnlyList<LauncherItem> Tools { get; }

    /// <summary>Raised when the tools tile is pressed, for the window to show the box.</summary>
    public event EventHandler? ToolBoxRequested;

    /// <summary>The icon's colour, opacity and size. Shared with the overview dialog, which is
    /// where they are changed.</summary>
    public LauncherAppearance Appearance { get; }

    /// <summary>Packing, unpacking and filing whatever is dropped on the icon.</summary>
    public DropCoordinator Drops { get; }

    /// <summary>
    /// Asks the menu to shut instantly rather than animate out. Raised just before something
    /// expensive is built on the UI thread, which an in-flight animation would stutter through.
    /// </summary>
    public event EventHandler? CollapseNowRequested;

    /// <summary>Drives the ring around the icon: something is up even with every window closed.</summary>
    public bool AnyRunning => _modules.Any(m => m.IsWorking);

    /// <summary>Drives the red ring: some feature wants attention, even with every window closed.</summary>
    public bool AnyAlert => _modules.Any(m => m.HasAlert);

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
    ///
    /// Refused, like a window's own close, while a feature says it must not be cut off.
    /// </summary>
    public async Task ExitAsync()
    {
        foreach (IFeatureModule module in _modules)
        {
            if (module.CloseBlockedReason is not { } reason) continue;

            MessageBox.Show(reason, module.Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (FeatureWindow window in _open.Values.ToArray()) window.ForceClose();
        _open.Clear();

        _overview?.Close();

        // Sequential, not parallel: teardown touches ETW sessions and sockets, and a failure in
        // one should not be racing another's.
        foreach (IFeatureModule module in _modules) await module.StopAsync();

        _settings.Save();
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

        _links = new LinksDialog(new LinksViewModel(WebLinks, FolderLinks));
        _links.Closed += (_, _) =>
        {
            _links = null;
            _settings.Links = WebLinks.Concat(FolderLinks).ToList();
            _settings.Save();
            RebuildItems();
        };
        _links.Show();
    }

    /// <summary>
    /// Opens a link's target off the UI thread.
    ///
    /// Starting a process takes a few hundred milliseconds, and reading the default browser out
    /// of the registry adds to it. Done here on the dispatcher, the launcher freezes for exactly
    /// that long right after the click - which is the moment the user is watching it. The work is
    /// handed to the pool and only the log line comes back.
    /// </summary>
    [RelayCommand]
    private async Task OpenFolder(LauncherLink? link)
    {
        if (link is null) return;

        CollapseNowRequested?.Invoke(this, EventArgs.Empty);

        string path = link.Url;
        FolderLauncher.Result opened = await Task.Run(() => FolderLauncher.Open(path));

        _activity.Add(link.Title, opened switch
        {
            FolderLauncher.Result.Opened => "폴더 열기",
            FolderLauncher.Result.Missing => $"폴더를 찾을 수 없습니다 — {path}",
            _ => "폴더 열기 실패",
        });
    }

    private async void OpenWebLink(LauncherLink link)
    {
        CollapseNowRequested?.Invoke(this, EventArgs.Empty);

        if (string.IsNullOrWhiteSpace(link.Url))
        {
            _activity.Add(link.Title, "주소가 비어 있습니다");
            return;
        }

        // The route is logged because the rungs below "popup" are the ones that produce a
        // support question - a tab instead of a window, or a sign-in page instead of the page -
        // and this is the only place that knows which one happened.
        (string url, int width, int height) = (link.Url, link.Width, link.Height);
        BrowserLauncher.Route route = await Task.Run(() => BrowserLauncher.OpenPopup(url, width, height));

        _activity.Add(link.Title, route switch
        {
            BrowserLauncher.Route.Popup => "열기 (팝업)",
            BrowserLauncher.Route.Tab => "열기 (탭 — 팝업 실패)",
            BrowserLauncher.Route.ElevatedTab => "열기 (관리자 브라우저 — 로그인이 풀릴 수 있음)",
            _ => "열기 실패 — 주소를 확인하세요",
        });
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
        window.Closed += (_, _) =>
        {
            _open.Remove(module);
            ReclaimMemory(module.Title);
        };
        _open[module] = window;
        window.Show();
    }

    /// <summary>
    /// Hands a closed feature's memory back to Windows straight away.
    ///
    /// Closing a feature only makes its objects unreachable - the GC decides when they actually
    /// go, and it runs when something allocates. With just the launcher left almost nothing does,
    /// so a closed image converter's decoded bitmaps (large-object-heap arrays, plus native WIC
    /// buffers only a finalizer frees) sat there indefinitely, and a PLC board went whenever
    /// something else happened to trigger a collection. A window closing is rare and the user is
    /// watching the number, so this is the one place a forced collection earns its pause.
    /// Aggressive mode also decommits what it frees instead of keeping it for later.
    ///
    /// Waits for ContextIdle first: Closed fires while the window's teardown is still unwinding,
    /// and until it has the view is reachable through it. The collection itself runs off the UI
    /// thread, so waiting for finalizers cannot block the one thread some of them need. The
    /// before/after figures go to the activity log, so a close that freed nothing shows up.
    /// </summary>
    private async void ReclaimMemory(string title)
    {
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);

        long before = PrivateBytes();
        await Task.Run(() =>
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        });

        _activity.Add(title, $"창 닫힘 · 메모리 {before / Megabyte} MB → {PrivateBytes() / Megabyte} MB");
    }

    private const long Megabyte = 1024 * 1024;

    private static long PrivateBytes()
    {
        using Process self = Process.GetCurrentProcess();
        return self.PrivateMemorySize64;
    }

    /// <summary>Records that Windows refused to open this window to drags from Explorer.</summary>
    public void NoteDropFilter(string detail) => _activity.Add("드롭", "메시지 " + detail);

    /// <summary>Records that a drag actually reached the launcher, and what it carried.</summary>
    public void NoteDrag(int fileCount, bool accepted)
        => _activity.Add("드롭", accepted
            ? $"항목 {fileCount}개 감지"
            : fileCount == 0 ? "파일이 아닌 내용" : "작업 중이라 거부");

    /// <summary>
    /// Runs the cutout mode and shows what it picked beside the point it was picked at.
    ///
    /// The menu shuts at once rather than animating out: the overlay paints a live capture of the
    /// screen, and a menu caught half-way through closing would be in it.
    /// </summary>
    /// <summary>
    /// The SAM cutout tile is switched off in this build: its model download and GPU load are
    /// more than the tool needs right now. The code stays; set this to true to bring the tile back.
    /// </summary>
    private static readonly bool CutoutEnabled = false;

    private async void StartCutout()
    {
        CollapseNowRequested?.Invoke(this, EventArgs.Empty);

        CutoutResult result = await CutoutSession.RunAsync(new ModelStore());

        if (result is { End: CutoutEnd.Picked, Image: { } image })
        {
            new CutoutViewer(image).ShowNear(result.CursorX, result.CursorY);
            _activity.Add("누끼", $"따기 {image.Width}×{image.Height}");
        }
        else if (result.End == CutoutEnd.Failed)
        {
            string error = result.Error ?? "알 수 없는 오류";
            _activity.Add("누끼", error);
            MessageBox.Show(error, "누끼", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnModuleChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AnyRunning));
        OnPropertyChanged(nameof(AnyAlert));
        _toolsItem?.RefreshRunning();

        if (sender is FeatureModule { State: FeatureState.Faulted } faulted)
        {
            _activity.Add(faulted.Title, "오류: " + (faulted.FaultMessage ?? "알 수 없음"));
        }
    }
}
