using System.ComponentModel;
using System.Windows;
using VisionSupport.Features;
using VisionSupport.Shell;

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

        SizeChanged += (_, e) => CaptionClip.Rect = new Rect(0, 0, e.NewSize.Width, CaptionHeight);

        // A maximised window has no corners to round - leaving the radius on cuts four notches
        // of desktop out of the screen corners.
        StateChanged += (_, _) => RootBorder.CornerRadius = WindowState == WindowState.Maximized
            ? new CornerRadius(0)
            : (CornerRadius)FindResource("RadiusWindow");
        Closing += OnClosing;
        // Letting go of the view here as well: WPF can keep a closed window referenced for a while
        // (input and focus bookkeeping), and it should not keep the whole feature with it.
        Closed += (_, _) =>
        {
            module.Changed -= OnModuleChanged;
            Host.Content = null;
        };

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
    /// A tool that is not working - no module up, nothing attached, no batch converting - goes
    /// away completely: threads, sockets, ETW sessions, the lot. One that is working keeps going
    /// in the background with only its view dropped. The tool's own run state decides, not the
    /// shell's State, which the tool's own start and stop buttons never touch.
    ///
    /// A tool can also refuse outright: an image batch can neither keep going without its window
    /// nor be cut off without leaving a half-written file. The window then stays and says why.
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

        if (Module.CloseBlockedReason is { } reason)
        {
            MessageBox.Show(this, reason, Module.Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _closeRequested = true;

        if (Module.IsWorking)
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

    private void OnModuleChanged(object? sender, EventArgs e) => RefreshCaption();

    private void RefreshCaption()
    {
        StatusText.Text = Module.StatusLine;
        StateDot.Fill = FeatureBrushes.For(Module);
    }
}
