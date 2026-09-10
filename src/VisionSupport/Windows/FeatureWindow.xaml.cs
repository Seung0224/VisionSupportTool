using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using VisionSupport.Features;

namespace VisionSupport.Windows;

/// <summary>
/// One feature's window.
///
/// The shell owns the chrome so the features do not have to: title, caption buttons, rounded
/// corners and - the part that matters - what closing means. Closing decides between letting go
/// of the whole feature and letting go of only its pixels, based on whether the feature is
/// actually running. That is the point of this design: a converter nobody is looking at should
/// cost nothing, and a PLC hub with a client attached should survive its window closing.
/// </summary>
public partial class FeatureWindow : Window
{
    /// <summary>Matches Theme/Dark.xaml's RadiusWindow. Kept in step by hand; it is one number.</summary>
    private const double WindowCornerRadius = 16;

    private const double CaptionHeight = 40;

    private bool _readyToClose;
    private bool _closeRequested;

    public FeatureWindow(IFeatureModule module)
    {
        InitializeComponent();

        Module = module;
        Title = module.Title;
        TitleText.Text = module.Title;
        Width = module.PreferredWindowSize.Width;
        Height = module.PreferredWindowSize.Height;
        Host.Content = module.GetOrCreateView();

        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        CloseButton.Click += (_, _) => Close();

        module.Changed += OnModuleChanged;

        SourceInitialized += (_, _) => WindowEffects.AttachRoundedCorners(this, WindowCornerRadius);
        SizeChanged += (_, e) => CaptionClip.Rect = new Rect(0, 0, e.NewSize.Width, CaptionHeight);
        Closing += OnClosing;
        Closed += (_, _) => module.Changed -= OnModuleChanged;

        RefreshCaption();
    }

    public IFeatureModule Module { get; }

    /// <summary>
    /// Closes without running the stop-or-release decision. Used on app exit, where every feature
    /// is being torn down anyway and asking each window again would only race the shutdown.
    /// </summary>
    public void ForceClose()
    {
        _readyToClose = true;
        Close();
    }

    /// <summary>
    /// Decides what closing costs.
    ///
    /// Stopped or faulted, the feature is not doing anything worth keeping, so it goes away
    /// completely - threads, sockets, ETW sessions, the lot. Running or paused, only the view is
    /// dropped and the work carries on in the background, visible on the launcher's ring and
    /// stoppable from the overview dialog.
    ///
    /// The dance with the flags is the one the old shell window needed too: awaiting the teardown
    /// synchronously deadlocks, because the stop path marshals state changes back to the UI
    /// thread that would be blocked waiting. So the close is cancelled, the teardown is genuinely
    /// awaited, and the window is closed afterwards - and a second Alt+F4 arriving during that
    /// wait must not start the whole thing again.
    ///
    /// The re-issued close has to go through the dispatcher rather than being called here.
    /// ReleaseView is synchronous and StopAsync usually finishes inline, so the await often does
    /// not yield at all - and a Close() raised from inside a Closing handler is re-entrant, which
    /// WPF drops while keeping this pass's Cancel. The window would stay open and take a second
    /// click on the X. Handing it to a later dispatcher turn lets this one unwind first.
    /// </summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_readyToClose) return;

        e.Cancel = true;
        if (_closeRequested) return;
        _closeRequested = true;

        if (Module.State is FeatureState.Running or FeatureState.Paused)
        {
            Module.ReleaseView();
        }
        else
        {
            await Module.StopAsync();
        }

        _readyToClose = true;
        await Dispatcher.BeginInvoke(Close);
    }

    private void OnModuleChanged(object? sender, EventArgs e)
    {
        RefreshCaption();

        // A feature that stopped on its own - from a button inside its own view - has dropped the
        // control this window is showing. Rebuild it, or the last chart stays on screen looking
        // live. Not while closing: that would resurrect the view we just released.
        if (!_closeRequested && Module.State is FeatureState.Stopped or FeatureState.Faulted)
        {
            Host.Content = Module.GetOrCreateView();
        }
    }

    private void RefreshCaption()
    {
        StatusText.Text = Module.StatusLine;

        string key = Module.State switch
        {
            FeatureState.Running or FeatureState.Starting => "StateRunning",
            FeatureState.Paused or FeatureState.Stopping => "StatePaused",
            FeatureState.Faulted => "StateFaulted",
            _ => "StateStopped",
        };

        StateDot.Fill = Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}
