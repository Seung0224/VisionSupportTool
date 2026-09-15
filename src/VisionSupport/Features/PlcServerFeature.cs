using System.Windows;
using System.Windows.Controls;
using VirtualPlcServer.ViewModels;
using VirtualPlcServer.Views;

namespace VisionSupport.Features;

/// <summary>
/// The virtual PLC hub as a hosted feature.
///
/// Modules are started and stopped from their own banners in the hub. The shell only asks whether
/// any of them is up: while one is, closing the window keeps the servers running for the VISION
/// client on the other end; once none is, closing saves the board and releases all of it.
/// </summary>
public sealed class PlcServerFeature : FeatureModule
{
    private MainViewModel? _viewModel;

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

    public override bool IsWorking => _viewModel is not null && _viewModel.RunningCount > 0;

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

    protected override async Task OnStopAsync()
    {
        if (_viewModel is null) return;

        await _viewModel.DisposeAllAsync();
        _viewModel = null;
    }
}
