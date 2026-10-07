using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Diagnostics.Runtime;
using VisionSupport.Features;
using VisionSupport.Launcher;
using VisionSupport.Overview;
using VisionSupport.Shell;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// A UI Automation client (a screen reader, or accessibility/security tooling that walks visible
/// windows) holds a native COM reference to whatever automation peers it touches. .NET cannot
/// collect a window while that reference exists, no matter what managed code drops - not
/// DataContext, not event handlers. Every open/close of OverviewDialog under such a client leaks
/// one whole window + view-model.
///
/// <see cref="Marshal.GetIUnknownForObject"/> manufactures the same kind of native reference a
/// real automation client would hold, so this test reproduces the leak deterministically instead
/// of depending on whether some automation client happens to be running.
/// </summary>
[Collection(StaAppCollection.Name)]
public class OverviewDialogAutomationTests
{
    private readonly StaAppFixture _app;

    public OverviewDialogAutomationTests(StaAppFixture app) => _app = app;

    [Fact]
    public void Closing_while_automation_holds_a_reference_still_frees_the_dialog() => _app.Run(() =>
    {
        EnsureShellResourcesMerged();

        bool IsTarget(ClrType t) => t.Name is "VisionSupport.Overview.OverviewDialog" or "VisionSupport.Overview.OverviewViewModel";
        int before = GcRootProbe.CountRootPaths(IsTarget);

        ShowTouchByAutomationAndClose();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int after = GcRootProbe.CountRootPaths(IsTarget);
        Assert.True(after <= before, $"expected no growth in live OverviewDialog/OverviewViewModel instances: before={before} after={after}");
    });

    /// <summary>Idempotent: several test classes share one Application on <see cref="StaAppFixture"/>.</summary>
    internal static void EnsureShellResourcesMerged()
    {
        ResourceDictionary resources = Application.Current!.Resources;
        if (resources.Contains("EmptyToVis")) return;

        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/VisionSupport;component/Theme/Dark.xaml")
        });
        resources["StateText"] = new StateTextConverter();
        resources["BoolToVis"] = new BoolToVisibilityConverter();
        resources["EmptyToVis"] = new EmptyToVisibilityConverter();
    }

    // Isolated in its own frame so the caller has nothing left over that would itself be a root.
    private static void ShowTouchByAutomationAndClose()
    {
        var modules = new List<IFeatureModule> { new FakeFeature() };
        var activity = new ActivityLog();
        var appearance = new LauncherAppearance(new LauncherSettings());

        var viewModel = new OverviewViewModel(modules, activity, appearance, DateTime.Now, _ => { });
        var dialog = new OverviewDialog(viewModel);

        dialog.Show();
        PumpToIdle();

        List<nint> comReferences = TouchEveryAutomationPeerLikeAScreenReaderWould(dialog);

        dialog.Close();
        PumpToIdle();
        PumpToIdle();

        foreach (nint unknown in comReferences) Marshal.Release(unknown);
    }

    /// <summary>
    /// Walks the whole automation tree and marshals each peer's provider across a real COM
    /// boundary, exactly what happens when an actual automation client enumerates a window - this
    /// is what puts a <c>RefCountedHandle</c> GC root on the peer.
    /// </summary>
    internal static List<nint> TouchEveryAutomationPeerLikeAScreenReaderWould(UIElement root)
    {
        MethodInfo providerFromPeer = typeof(AutomationPeer).GetMethod(
            "ProviderFromPeer", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var comReferences = new List<nint>();

        void Walk(AutomationPeer peer)
        {
            if (providerFromPeer.Invoke(peer, new object[] { peer }) is IRawElementProviderSimple provider)
                comReferences.Add(Marshal.GetIUnknownForObject(provider));

            foreach (AutomationPeer child in peer.GetChildren() ?? new List<AutomationPeer>())
                Walk(child);
        }

        AutomationPeer? rootPeer = UIElementAutomationPeer.CreatePeerForElement(root);
        if (rootPeer is not null) Walk(rootPeer);

        return comReferences;
    }

    internal static void PumpToIdle()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class FakeFeature : FeatureModule
    {
        public override string Title => "테스트";

        public override string Description => "누수 검증용";

        public override bool IsWorking => false;

        protected override UserControl CreateView() => new UserControl();

        protected override Task OnStopAsync() => Task.CompletedTask;
    }
}
