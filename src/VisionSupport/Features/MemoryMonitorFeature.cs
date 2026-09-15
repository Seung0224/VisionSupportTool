using System.Windows;
using System.Windows.Controls;
using MemMon;
using MemMon.ViewModels;

namespace VisionSupport.Features;

/// <summary>
/// The managed-heap monitor as a hosted feature.
///
/// Attaching and detaching happen from the monitor's own toolbar; the shell only asks whether it
/// is attached. Stop is the one that matters for the shell's health: it detaches, closes the ETW
/// sessions and releases the target handle, because an ETW session outlives the process that made
/// it and a leaked one costs the whole machine until reboot.
/// </summary>
public sealed class MemoryMonitorFeature : FeatureModule
{
    private MainViewModel? _viewModel;

    public override string Title => "메모리 모니터";

    public override string Description
        => "대상 프로세스의 관리 힙·GC 일시정지·네이티브 할당을 추적하고 스냅샷을 비교합니다.";

    public override string Glyph => "Memory";

    public override Size PreferredWindowSize => new(1280, 820);

    public override string StatusLine => _viewModel?.Status ?? string.Empty;

    public override bool IsWorking => _viewModel is { IsAttached: true };

    /// <summary>
    /// Built on first use: constructing it kicks off a scan of every process on the machine,
    /// which should wait until someone actually opens the monitor.
    /// </summary>
    private MainViewModel ViewModel
    {
        get
        {
            if (_viewModel is not null) return _viewModel;

            _viewModel = new MainViewModel();
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(MainViewModel.Status) or nameof(MainViewModel.IsAttached))
                {
                    RaiseChanged();
                }
            };
            return _viewModel;
        }
    }

    protected override UserControl CreateView()
    {
        var view = new MonitorView(ViewModel);
        view.WindowOpened += (_, window) => TrackWindow(window);
        return view;
    }

    protected override Task OnStopAsync()
    {
        // Dispose detaches: it stops the timer, cancels any running snapshot, tears down both
        // ETW sessions and releases the ClrMD attach. Safe to call twice - Detach guards on null.
        _viewModel?.Dispose();
        _viewModel = null;
        return Task.CompletedTask;
    }
}
