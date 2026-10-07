using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Diagnostics.Runtime;
using VisionSupport.Features;
using VisionSupport.Windows;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Same coverage as <see cref="OverviewDialogAutomationTests"/>, for the window every feature
/// opens into. See that test's remarks for why this has to be reproduced with a real COM
/// reference rather than a WeakReference check.
/// </summary>
[Collection(StaAppCollection.Name)]
public class FeatureWindowAutomationTests
{
    private readonly StaAppFixture _app;

    public FeatureWindowAutomationTests(StaAppFixture app) => _app = app;

    [Fact]
    public void Closing_while_automation_holds_a_reference_still_frees_the_window() => _app.Run(() =>
    {
        OverviewDialogAutomationTests.EnsureShellResourcesMerged();

        bool IsTarget(ClrType t) => t.Name == "VisionSupport.Windows.FeatureWindow";
        int before = GcRootProbe.CountRootPaths(IsTarget);

        ShowTouchByAutomationAndClose();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int after = GcRootProbe.CountRootPaths(IsTarget);
        Assert.True(after <= before, $"expected no growth in live FeatureWindow instances: before={before} after={after}");
    });

    private static void ShowTouchByAutomationAndClose()
    {
        var module = new FakeFeature();
        var window = new FeatureWindow(module);

        window.Show();
        OverviewDialogAutomationTests.PumpToIdle();

        List<nint> comReferences = OverviewDialogAutomationTests.TouchEveryAutomationPeerLikeAScreenReaderWould(window);

        window.Close();
        OverviewDialogAutomationTests.PumpToIdle();
        OverviewDialogAutomationTests.PumpToIdle();

        foreach (nint unknown in comReferences) Marshal.Release(unknown);
    }

    private sealed class FakeFeature : FeatureModule
    {
        public override string Title => "테스트";

        public override string Description => "누수 검증용";

        public override bool IsWorking => false;

        // A real feature view (PLC module list, memory monitor's process list, ...) has an
        // ItemsControl generating rows from a DataTemplate - exactly the shape that put the
        // native automation reference on OverviewDialog. An empty UserControl would not
        // reproduce that, so this builds the same minimal shape by hand.
        protected override UserControl CreateView()
        {
            var itemsControl = new ItemsControl { ItemsSource = new[] { "row" } };
            itemsControl.ItemTemplate = new DataTemplate
            {
                VisualTree = new FrameworkElementFactory(typeof(TextBlock))
            };
            return new UserControl { Content = itemsControl };
        }

        protected override Task OnStopAsync() => Task.CompletedTask;
    }
}
