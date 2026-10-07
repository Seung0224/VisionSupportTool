using System.Runtime.InteropServices;
using MemMon;
using Microsoft.Diagnostics.Runtime;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Same coverage as <see cref="OverviewDialogAutomationTests"/>, for the memory monitor's GC
/// glossary window. See that test's remarks for why this has to be reproduced with a real COM
/// reference rather than a WeakReference check.
/// </summary>
[Collection(StaAppCollection.Name)]
public class GcReferenceWindowAutomationTests
{
    private readonly StaAppFixture _app;

    public GcReferenceWindowAutomationTests(StaAppFixture app) => _app = app;

    [Fact]
    public void Closing_while_automation_holds_a_reference_still_frees_the_window() => _app.Run(() =>
    {
        OverviewDialogAutomationTests.EnsureShellResourcesMerged();

        bool IsTarget(ClrType t) => t.Name == "MemMon.GcReferenceWindow";
        int before = GcRootProbe.CountRootPaths(IsTarget);

        ShowTouchByAutomationAndClose();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int after = GcRootProbe.CountRootPaths(IsTarget);
        Assert.True(after <= before, $"expected no growth in live GcReferenceWindow instances: before={before} after={after}");
    });

    private static void ShowTouchByAutomationAndClose()
    {
        var window = new GcReferenceWindow();

        window.Show();
        OverviewDialogAutomationTests.PumpToIdle();

        List<nint> comReferences = OverviewDialogAutomationTests.TouchEveryAutomationPeerLikeAScreenReaderWould(window);

        window.Close();
        OverviewDialogAutomationTests.PumpToIdle();
        OverviewDialogAutomationTests.PumpToIdle();

        foreach (nint unknown in comReferences) Marshal.Release(unknown);
    }
}
