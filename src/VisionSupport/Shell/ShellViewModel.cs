using System.Collections.ObjectModel;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSupport.Features;
using VisionSupport.Idle;

namespace VisionSupport.Shell;

/// <summary>
/// Drives the nav rail and the command bar. Owns the feature list for the life of the process:
/// features are started and stopped, but never created or destroyed, so a feature's run state
/// survives navigating away from its page - which is the point of a tool that sits open all day.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IReadOnlyList<IFeatureModule> _modules;

    [ObservableProperty]
    private NavItem? _selectedItem;

    [ObservableProperty]
    private UserControl? _content;

    /// <summary>Collapses the rail to a narrow strip; the hamburger toggles it.</summary>
    [ObservableProperty]
    private bool _isNavExpanded = true;

    public ShellViewModel(IReadOnlyList<IFeatureModule> modules)
    {
        _modules = modules;

        var idleViewModel = new IdleViewModel(modules, Activity, NavigateTo);
        Items.Add(new NavItem("대기화면", () => new IdleView { DataContext = idleViewModel }));

        foreach (IFeatureModule module in modules)
        {
            Items.Add(new NavItem(module.Title, module.GetOrCreateView, module));
            module.Changed += OnModuleChanged;
            Activity.Add(module.Title, "준비됨");
        }

        SelectedItem = Items[0];
    }

    public ObservableCollection<NavItem> Items { get; } = new();

    public ActivityLog Activity { get; } = new();

    public IFeatureModule? CurrentModule => SelectedItem?.Module;

    public string CurrentTitle => SelectedItem?.Title ?? string.Empty;

    public FeatureState CurrentState => CurrentModule?.State ?? FeatureState.Stopped;

    public string CurrentStatusLine => CurrentModule?.StatusLine ?? string.Empty;

    /// <summary>The command bar only makes sense on a feature page; the idle page has nothing to run.</summary>
    public bool HasCommands => CurrentModule is not null;

    public bool CanStart => CurrentModule is { } m
        && m.State is FeatureState.Stopped or FeatureState.Paused or FeatureState.Faulted;

    public bool CanPause => CurrentModule is { CanPause: true, State: FeatureState.Running };

    public bool CanStop => CurrentModule is { } m
        && m.State is FeatureState.Running or FeatureState.Paused or FeatureState.Faulted;

    [RelayCommand]
    private void ToggleNav() => IsNavExpanded = !IsNavExpanded;

    [RelayCommand]
    private void Select(NavItem item) => SelectedItem = item;

    [RelayCommand]
    private async Task Start()
    {
        if (CurrentModule is not { } module) return;
        bool resuming = module.State == FeatureState.Paused;
        await module.StartAsync();
        Activity.Add(module.Title, resuming ? "재개" : "실행");
    }

    [RelayCommand]
    private async Task Pause()
    {
        if (CurrentModule is not { } module) return;
        await module.PauseAsync();
        Activity.Add(module.Title, "정지");
    }

    [RelayCommand]
    private async Task Stop()
    {
        if (CurrentModule is not { } module) return;
        await module.StopAsync();
        Activity.Add(module.Title, "종료");
    }

    /// <summary>
    /// Stops every feature on the way out. Runs sequentially rather than in parallel: teardown
    /// touches ETW sessions and sockets, and a failure in one should not be racing another's.
    /// </summary>
    public async Task ShutdownAsync()
    {
        foreach (IFeatureModule module in _modules)
        {
            await module.StopAsync();
        }
    }

    private void NavigateTo(IFeatureModule module)
    {
        NavItem? target = Items.FirstOrDefault(i => ReferenceEquals(i.Module, module));
        if (target is not null) SelectedItem = target;
    }

    partial void OnSelectedItemChanged(NavItem? oldValue, NavItem? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is null) return;

        newValue.IsSelected = true;
        Content = newValue.BuildView();
        RaiseCommandBarChanged();
    }

    private void OnModuleChanged(object? sender, EventArgs e)
    {
        if (sender is not IFeatureModule module) return;

        // A stopped feature has dropped its view, so the page on screen is now detached from the
        // module that owns it. Rebuild it, otherwise the last chart stays up looking live.
        if (ReferenceEquals(module, CurrentModule)
            && module.State is FeatureState.Stopped or FeatureState.Faulted
            && SelectedItem is { } item)
        {
            Content = item.BuildView();
        }

        if (module.State == FeatureState.Faulted && module is FeatureModule concrete)
        {
            Activity.Add(module.Title, "오류: " + (concrete.FaultMessage ?? "알 수 없음"));
        }

        if (ReferenceEquals(module, CurrentModule)) RaiseCommandBarChanged();
    }

    private void RaiseCommandBarChanged()
    {
        OnPropertyChanged(nameof(CurrentModule));
        OnPropertyChanged(nameof(CurrentTitle));
        OnPropertyChanged(nameof(CurrentState));
        OnPropertyChanged(nameof(CurrentStatusLine));
        OnPropertyChanged(nameof(HasCommands));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanStop));
        StartCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }
}
