using System.Windows;
using System.Windows.Controls;
using VisionSupport.Wireshark;
using VisionSupport.Wireshark.ViewModels;

namespace VisionSupport.Features;

/// <summary>
/// The communication monitor as a hosted feature.
///
/// Capture and CXP watching start and stop from the monitor's own toolbar; the shell only asks
/// whether either is running and whether anything is wrong. Stop is what matters for the shell's
/// health: it ends the ETW session and pktmon, because both outlive the process that started
/// them and a leaked capture keeps costing the machine until someone runs `pktmon stop`.
/// </summary>
public sealed class WiresharkFeature : FeatureModule
{
    private WiresharkViewModel? _viewModel;

    public override string Title => "통신 모니터";

    public override string Description
        => "PLC(MC·ADS)·GigE 이더넷과 CXP 카메라의 송수신과 끊김·지연·드롭을 감시합니다.";

    public override string Glyph => "LanConnect";

    public override Size PreferredWindowSize => new(1280, 860);

    public override string StatusLine => _viewModel?.Status ?? string.Empty;

    public override bool IsWorking => _viewModel is { IsWorking: true };

    public override bool HasAlert => _viewModel is { HasAlert: true };

    private WiresharkViewModel ViewModel
    {
        get
        {
            if (_viewModel is not null) return _viewModel;

            _viewModel = new WiresharkViewModel();
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(WiresharkViewModel.Status)
                    or nameof(WiresharkViewModel.IsWorking)
                    or nameof(WiresharkViewModel.HasAlert))
                {
                    RaiseChanged();
                }
            };
            return _viewModel;
        }
    }

    protected override UserControl CreateView() => new WiresharkView(ViewModel);

    protected override Task OnStopAsync()
    {
        // Dispose stops the capture (ETW session, then `pktmon stop`) and the CXP poll loop, and
        // saves settings. Safe to call twice.
        _viewModel?.Dispose();
        _viewModel = null;
        return Task.CompletedTask;
    }
}
