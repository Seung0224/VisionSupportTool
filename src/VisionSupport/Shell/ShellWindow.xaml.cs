using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VisionSupport.Shell;

public partial class ShellWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    private bool _readyToClose;
    private bool _closeRequested;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public ShellWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => UseDarkTitleBar();
        Closing += OnClosing;
    }

    /// <summary>WPF does not theme the non-client area, so ask DWM for the dark title bar.</summary>
    private void UseDarkTitleBar()
    {
        int enabled = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,
            DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    /// <summary>
    /// Stops every feature before the window goes away.
    ///
    /// Two hazards, both learned the hard way in the PLC server this shell absorbed. Waiting on
    /// the async teardown synchronously deadlocks: the stop path marshals state changes back to
    /// the UI thread, which is the thread that would be blocked waiting. So the close is
    /// cancelled, the teardown is genuinely awaited, and the app is shut down afterwards. And
    /// while that runs the user can hit X or Alt+F4 again, re-entering this handler; the second
    /// pass would call Shutdown on an already-closing window, so the first thing it does is take
    /// a flag.
    /// </summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_readyToClose) return;

        e.Cancel = true;
        if (_closeRequested) return;
        _closeRequested = true;

        if (DataContext is ShellViewModel viewModel)
        {
            await viewModel.ShutdownAsync();
        }

        _readyToClose = true;
        Application.Current.Shutdown();
    }
}
