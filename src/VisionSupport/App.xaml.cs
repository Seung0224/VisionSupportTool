using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using VisionSupport.Archive;
using VisionSupport.Features;
using VisionSupport.Launcher;
using VisionSupport.Shell;

namespace VisionSupport;

public partial class App : Application
{
    private readonly ActivityLog _activity = new();
    private readonly DateTime _startedAt = DateTime.Now;

    private LauncherSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!EnsureElevated()) return;

        // Feature windows come and go; the launcher is the only thing that stays. Without this
        // the process would exit the moment the last feature window closed.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // This process is meant to stay up for days with features starting and stopping under
        // it. A feature that throws on a background task or in an event handler must not be
        // able to take the launcher with it, so those escape hatches are closed here.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _settings = LauncherSettings.Load(LauncherSettings.DefaultPath);
        PlaceIcon();

        // Before any window exists: an elevated process is not a drop target until the messages
        // Explorer sends are let back through.
        DropElevation.AllowDropMessages();

        var appearance = new LauncherAppearance(_settings);
        var launcher = new LauncherViewModel(CreateModules(), _activity, _settings, appearance, _startedAt);
        new LauncherWindow(launcher, _settings).Show();
    }

    /// <summary>
    /// Parks a first-run icon near the bottom-right, and rescues one left on a monitor that is no
    /// longer attached - there would be no way back to it but deleting the settings file.
    /// </summary>
    private void PlaceIcon()
    {
        var iconSize = new Size(72, 72);
        var fallback = new Point(
            SystemParameters.WorkArea.Right - 110,
            SystemParameters.WorkArea.Bottom - 130);

        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        Point placed = _settings is { IconLeft: 0, IconTop: 0 }
            ? fallback
            : LauncherSettings.ConstrainToScreen(
                new Point(_settings.IconLeft, _settings.IconTop), iconSize, virtualScreen, fallback);

        _settings.IconLeft = placed.X;
        _settings.IconTop = placed.Y;
    }

    /// <summary>
    /// Refuses to run without administrator rights, relaunching elevated instead.
    ///
    /// app.manifest already requests elevation, so in a normal launch this never fires - Windows
    /// has shown the UAC prompt before any of this code runs. It exists because the manifest is
    /// a build-time guarantee: strip it, or launch a build that was produced without it, and the
    /// shell would come up looking fine while the memory monitor silently lost its ETW sessions
    /// and could not attach to VISION. Failing loudly at startup beats a half-working tool.
    /// </summary>
    /// <returns>True to carry on starting; false when this process is handing over and exiting.</returns>
    private bool EnsureElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return true;

        string? exePath = Environment.ProcessPath;
        if (exePath is not null)
        {
            try
            {
                // "runas" is what raises the UAC prompt; it needs ShellExecute, which is off by
                // default for a .NET process.
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, Verb = "runas" });
                Shutdown();
                return false;
            }
            catch (Win32Exception)
            {
                // The user dismissed the UAC prompt. Fall through to the explanation below
                // rather than starting degraded.
            }
        }

        MessageBox.Show(
            "VISION 지원툴은 관리자 권한이 필요합니다.\n\n" +
            "메모리 모니터가 ETW 세션을 만들고 관리자 권한으로 실행 중인 VISION에 attach하려면 " +
            "이 권한이 있어야 합니다. 권한 없이 실행하면 해당 기능이 동작하지 않습니다.\n\n" +
            "프로그램을 마우스 오른쪽 버튼으로 눌러 '관리자 권한으로 실행'을 선택해 주세요.",
            "관리자 권한 필요", MessageBoxButton.OK, MessageBoxImage.Warning);

        Shutdown();
        return false;
    }

    /// <summary>
    /// The features this shell hosts, in nav rail order. The only place that knows which
    /// features exist - adding one is a line here plus its module class.
    /// </summary>
    private static IReadOnlyList<IFeatureModule> CreateModules() => new List<IFeatureModule>
    {
        new MemoryMonitorFeature(),
        new PlcServerFeature(),
        new ImageConverterFeature(),
    };

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _activity.Add("지원툴", "처리되지 않은 오류: " + e.Exception.Message);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _activity.Add("지원툴", "백그라운드 작업 오류: " + e.Exception.GetBaseException().Message);
        e.SetObserved();
    }
}
