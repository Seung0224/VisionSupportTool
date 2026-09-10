using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using VisionSupport.Features;
using VisionSupport.Shell;

namespace VisionSupport;

public partial class App : Application
{
    private ShellViewModel? _shell;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!EnsureElevated()) return;

        // This process is meant to stay up for days with features starting and stopping under
        // it. A feature that throws on a background task or in an event handler must not be
        // able to take the shell with it, so those escape hatches are closed here.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _shell = new ShellViewModel(CreateModules());
        new ShellWindow { DataContext = _shell }.Show();
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
        _shell?.Activity.Add("지원툴", "처리되지 않은 오류: " + e.Exception.Message);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _shell?.Activity.Add("지원툴", "백그라운드 작업 오류: " + e.Exception.GetBaseException().Message);
        e.SetObserved();
    }
}
