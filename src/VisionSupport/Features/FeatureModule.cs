using System.Windows;
using System.Windows.Controls;

namespace VisionSupport.Features;

/// <summary>
/// Base class carrying the isolation guarantees every hosted feature owes the shell, so each
/// feature only has to implement what it actually does.
///
/// What this class is for: the shell is meant to stay up for days while features are started,
/// paused and stopped underneath it. A feature that throws on the way down, leaks a background
/// thread, or leaves one of its own windows open would leak that damage into a process the user
/// is not restarting. So teardown is funnelled through here - <see cref="StopAsync"/> cancels the
/// run token, calls the feature's own teardown, closes every window the feature opened, and drops
/// the view, swallowing failures into <see cref="FeatureState.Faulted"/> rather than propagating
/// them.
/// </summary>
public abstract class FeatureModule : IFeatureModule
{
    /// <summary>Serialises Start/Pause/Resume/Stop. Without it a double-click on the command
    /// bar can run a start and a stop against the same half-built state.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Detail windows the feature opened. Closed on stop so none outlive the feature.</summary>
    private readonly List<Window> _ownedWindows = new();

    private CancellationTokenSource? _cts;
    private UserControl? _view;
    private FeatureState _state = FeatureState.Stopped;

    public abstract string Title { get; }

    public abstract string Description { get; }

    public virtual bool CanPause => true;

    public virtual string StatusLine => string.Empty;

    public virtual string Glyph => "None";

    public virtual Size PreferredWindowSize => new(1100, 720);

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

    /// <summary>
    /// Cancelled by <see cref="StopAsync"/>. Every loop, timer and long task the feature starts
    /// must observe this - it is the one signal that says "let go of everything now".
    /// </summary>
    protected CancellationToken RunToken => _cts?.Token ?? CancellationToken.None;

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

    public async Task StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_state is FeatureState.Running or FeatureState.Starting) return;
            if (_state == FeatureState.Paused)
            {
                await ResumeCore().ConfigureAwait(true);
                return;
            }

            State = FeatureState.Starting;
            _cts = new CancellationTokenSource();
            await OnStartAsync(_cts.Token).ConfigureAwait(true);
            State = FeatureState.Running;
        }
        catch (Exception ex)
        {
            // A failed start has usually taken half the resources it wanted, so tear the rest
            // down rather than leaving the feature in a state nothing can clean up later.
            await SafeTeardown().ConfigureAwait(true);
            // Set the message before the state: the state setter is what notifies the shell, and
            // it should not fire while the reason for the fault is still the previous one.
            FaultMessage = ex.Message;
            State = FeatureState.Faulted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PauseAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_state != FeatureState.Running || !CanPause) return;
            await OnPauseAsync().ConfigureAwait(true);
            State = FeatureState.Paused;
        }
        catch (Exception ex)
        {
            FaultMessage = ex.Message;
            State = FeatureState.Faulted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResumeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            await ResumeCore().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            FaultMessage = ex.Message;
            State = FeatureState.Faulted;
        }
        finally
        {
            _gate.Release();
        }
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
            if (_state == FeatureState.Stopped) return;
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

    protected abstract Task OnStartAsync(CancellationToken ct);

    protected abstract Task OnPauseAsync();

    protected abstract Task OnResumeAsync();

    /// <summary>
    /// Release everything the feature took: threads, sockets, ETW sessions, native handles.
    /// Called with the run token already cancelled. Throwing here marks the feature faulted but
    /// never reaches the shell.
    /// </summary>
    protected abstract Task OnStopAsync();

    private async Task ResumeCore()
    {
        if (_state != FeatureState.Paused) return;
        await OnResumeAsync().ConfigureAwait(true);
        State = FeatureState.Running;
    }

    /// <summary>Runs the teardown sequence, reporting whether it completed without error.</summary>
    private async Task<bool> SafeTeardown()
    {
        bool clean = true;

        try
        {
            _cts?.Cancel();
        }
        catch (Exception ex)
        {
            FaultMessage = ex.Message;
            clean = false;
        }

        try
        {
            await OnStopAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            FaultMessage = ex.Message;
            clean = false;
        }

        try
        {
            _cts?.Dispose();
        }
        catch
        {
            // Disposing an already-disposed source is harmless.
        }
        _cts = null;

        // Dispose the ViewModel first, then hand the rest to ReleaseView: a full stop is a view
        // release plus letting go of what the view was showing. The windows close even if the
        // feature's own teardown failed - they are the most visible leftover, and leaving them
        // open makes a stopped feature look like it is still running.
        (_view?.DataContext as IDisposable)?.Dispose();
        ReleaseView();

        return clean;
    }
}
