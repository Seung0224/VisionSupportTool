using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Protocols.OpcUa;
using VirtualPlcServer.Views;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Same coverage as <see cref="OverviewDialogAutomationTests"/>, for the PLC server's detail
/// monitor window. See that test's remarks for why this has to be reproduced with a real COM
/// reference rather than a WeakReference check.
/// </summary>
[Collection(StaAppCollection.Name)]
public class PlcMonitorWindowAutomationTests
{
    private readonly StaAppFixture _app;

    public PlcMonitorWindowAutomationTests(StaAppFixture app) => _app = app;

    [Fact]
    public void Closing_while_automation_holds_a_reference_still_frees_the_window() => _app.Run(() =>
    {
        bool IsTarget(ClrType t) => t.Name == "VirtualPlcServer.Views.PlcMonitorWindow";
        int before = GcRootProbe.CountRootPaths(IsTarget);

        ShowTouchByAutomationAndClose();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int after = GcRootProbe.CountRootPaths(IsTarget);
        Assert.True(after <= before, $"expected no growth in live PlcMonitorWindow instances: before={before} after={after}");
    });

    private static void ShowTouchByAutomationAndClose()
    {
        var server = new OpcUaPlcServer(new OpcUaServerConfig { Port = 48413, ApplicationName = "automation-test" });
        var module = new PlcServerModule(server, "automation-test");
        var window = new PlcMonitorWindow(module);

        window.Show();
        OverviewDialogAutomationTests.PumpToIdle();

        List<nint> comReferences = OverviewDialogAutomationTests.TouchEveryAutomationPeerLikeAScreenReaderWould(window);

        window.Close();
        OverviewDialogAutomationTests.PumpToIdle();
        OverviewDialogAutomationTests.PumpToIdle();

        foreach (nint unknown in comReferences) Marshal.Release(unknown);
    }
}
