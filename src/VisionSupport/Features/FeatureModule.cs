using System.Windows;
using System.Windows.Controls;

namespace VisionSupport.Features;

/// <summary>
/// Base class carrying the isolation guarantees every hosted feature owes the shell, so each
/// feature only has to implement what it actually does.
///
/// What this class is for: the shell is meant to stay up for days while features are opened,
/// used and closed underneath it. A feature that throws on the way down, leaks a background
/// thread, or leaves one of its own windows open would leak that damage into a process the user
/// is not restarting. So teardown is funnelled through here - <see cref="StopAsync"/> calls the
/// feature's own teardown, closes every window the feature opened, and drops the view,
/// swallowing failures into <see cref="FeatureState.Faulted"/> rather than propagating them.
/// </summary>
public abstract class FeatureModule : IFeatureModule
{
    /// <summary>Serialises stops. A window's close and the launcher's exit can both be tearing the
    /// same feature down, and neither should run against the other's half-released state.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Detail windows the feature opened. Closed on stop so none outlive the feature.</summary>
    private readonly List<Window> _ownedWindows = new();

    private UserControl? _view;
    private FeatureState _state = FeatureState.Stopped;

    public abstract string Title { get; }

    public abstract string Description { get; }

    public virtual string StatusLine => string.Empty;

    public virtual string Glyph => "None";

    public virtual Size PreferredWindowSize => new(1100, 720);

    public abstract bool IsWorking { get; }

    public virtual string? CloseBlockedReason => null;

    public FeatureState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            RaiseChanged();
        }
    }

    public event EventHandler? Changed;

    public UserControl GetOrCreateView() => _view ??= CreateView();

    /// <summary>
    /// Drops the view and everything hanging off it, leaving the feature running.
    ///
    /// Detail windows go with it: they read live state out of the feature, and one left behind
    /// after its parent window closed is a floating panel with no way back to what opened it.
    /// The view's DataContext is deliberately not disposed here - that is the feature's own
    /// ViewModel, and it is the thing being kept alive.
    /// </summary>
    public void ReleaseView()
    {
        foreach (Window window in _ownedWindows.ToArray())
        {
            try
            {
                window.Close();
            }
            catch
            {
                // A window already closing throws; nothing here is worth failing over.
            }
        }
        _ownedWindows.Clear();

        (_view as IDisposable)?.Dispose();
        _view = null;
    }

    /// <summary>
    /// Full teardown. Never throws - a feature that cannot stop cleanly becomes
    /// <see cref="FeatureState.Faulted"/> and the shell carries on.
    /// </summary>
    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            // No early return for Stopped: it is the only resting state, and opening a window
            // builds the ViewModel, so a Stopped feature can still be holding all of it.
            State = FeatureState.Stopping;
            bool clean = await SafeTeardown().ConfigureAwait(true);
            State = clean ? FeatureState.Stopped : FeatureState.Faulted;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Set when a transition failed, so the shell can show why rather than just "Faulted".</summary>
    public string? FaultMessage { get; private set; }

    /// <summary>
    /// Registers a window the feature opened so teardown can close it. A detail window that
    /// outlived its feature would keep querying a stopped server and throw on a dead session.
    /// </summary>
    protected void TrackWindow(Window window)
    {
        _ownedWindows.Add(window);
        window.Closed += (_, _) => _ownedWindows.Remove(window);
    }

    protected void RaiseChanged()
    {
        // Feature state changes come off socket threads, ETW pump threads and task continuations.
        // Marshalling here means every consumer - nav rail, command bar, idle cards - is plain
        // UI-thread code with no dispatcher juggling of its own.
        Application? app = Application.Current;
        if (app is null || app.Dispatcher.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        app.Dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>Builds the feature's screen. Called again after a stop, so it must not assume
    /// it only ever runs once.</summary>
    protected abstract UserControl CreateView();

    /// <summary>
    /// Release everything the feature took: threads, sockets, ETW sessions, native handles.
    /// Throwing here marks the feature faulted but never reaches the shell.
    /// </summary>
    protected abstract Task OnStopAsync();

    /// <summary>Runs the teardown sequence, reporting whether it completed without error.</summary>
    private async Task<bool> SafeTeardown()
    {
        bool clean = true;

        try
        {
            await OnStopAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            FaultMessage = ex.Message;
            clean = false;
        }

        // Dispose the ViewModel first, then hand the rest to ReleaseView: a full stop is a view
        // release plus letting go of what the view was showing. The windows close even if the
        // feature's own teardown failed - they are the most visible leftover, and leaving them
        // open makes a stopped feature look like it is still running.
        (_view?.DataContext as IDisposable)?.Dispose();
        ReleaseView();

        return clean;
    }
}
