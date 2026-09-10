using System.Windows;
using System.Windows.Controls;
using VirtualPlcServer.ViewModels;
using VirtualPlcServer.Views;

namespace VisionSupport.Features;

/// <summary>
/// The virtual PLC hub as a hosted feature.
///
/// The hub already has its own per-module start/stop buttons, so the shell's three commands act
/// across all of them at once: start brings every module up, pause takes them down but remembers
/// which were up, and stop saves state and releases the hub entirely. Pausing keeps node values -
/// a module's StopAsync closes its sockets without touching its map - so resuming puts the
/// servers back exactly as they were, which is the point when a VISION client is mid-test.
/// </summary>
public sealed class PlcServerFeature : FeatureModule
{
    private MainViewModel? _viewModel;

    /// <summary>Modules that were running when the feature was paused, so resume restores that
    /// set rather than starting everything the hub happens to hold.</summary>
    private IReadOnlyList<Guid> _runningWhenPaused = Array.Empty<Guid>();

    public override string Title => "PLC 서버";

    public override string Description
        => "MC · MC-TCP · OPC UA · ADS 가상 PLC 서버와 시나리오 엔진. VISION이 붙을 상대를 대신합니다.";

    public override string Glyph => "ServerNetwork";

    public override Size PreferredWindowSize => new(1180, 760);

    public override string StatusLine
    {
        get
        {
            if (_viewModel is null) return string.Empty;
            int total = _viewModel.Modules.Count;
            return total == 0 ? "모듈 없음" : $"모듈 {total}개 · 가동 중 {_viewModel.RunningCount}개";
        }
    }

    /// <summary>
    /// Built on first use rather than in the constructor: constructing it reads the saved module
    /// list off disk and rebuilds every server, which should not happen just because the shell
    /// started.
    /// </summary>
    private MainViewModel ViewModel
    {
        get
        {
            if (_viewModel is not null) return _viewModel;

            _viewModel = new MainViewModel();
            _viewModel.DetailWindowOpened += (_, window) => TrackWindow(window);
            _viewModel.ModulesChanged += (_, _) => RaiseChanged();
            return _viewModel;
        }
    }

    protected override UserControl CreateView() => new PlcServerView(ViewModel);

    protected override Task OnStartAsync(CancellationToken ct) => ViewModel.StartAllAsync();

    protected override async Task OnPauseAsync()
        => _runningWhenPaused = await ViewModel.StopAllAsync();

    protected override async Task OnResumeAsync()
    {
        await ViewModel.StartAsync(_runningWhenPaused);
        _runningWhenPaused = Array.Empty<Guid>();
    }

    protected override async Task OnStopAsync()
    {
        if (_viewModel is null) return;

        await _viewModel.DisposeAllAsync();
        _viewModel = null;
        _runningWhenPaused = Array.Empty<Guid>();
    }
}
