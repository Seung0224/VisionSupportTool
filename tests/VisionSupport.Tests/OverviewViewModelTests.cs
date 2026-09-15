using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using VisionSupport.Features;
using VisionSupport.Launcher;
using VisionSupport.Overview;
using VisionSupport.Shell;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Reopening the overview dialog must not leave the previous open's rows subscribed to their
/// module's Changed event forever - modules live for the process lifetime, so an undropped
/// subscription there is a permanent leak: one FeatureRow per module, per open/close cycle.
/// </summary>
public class OverviewViewModelTests
{
    [Fact]
    public void Deactivating_lets_its_rows_be_collected() => RunSta(() =>
    {
        var module = new FakeFeature();
        var modules = new List<IFeatureModule> { module };
        var activity = new ActivityLog();
        var appearance = new LauncherAppearance(new LauncherSettings());

        WeakReference weakRow = MakeAndDeactivate(modules, activity, appearance);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weakRow.IsAlive);
    });

    // Isolated in its own frame so the row has no reachable local left once it returns - the
    // opened/deactivated OverviewViewModel and its FeatureRow must survive only through
    // module.Changed, which is exactly what this test is checking.
    private static WeakReference MakeAndDeactivate(
        IReadOnlyList<IFeatureModule> modules, ActivityLog activity, LauncherAppearance appearance)
    {
        var viewModel = new OverviewViewModel(modules, activity, appearance, DateTime.Now, _ => { });
        viewModel.Activate();
        var weakRow = new WeakReference(viewModel.Rows[0]);
        viewModel.Deactivate();
        return weakRow;
    }

    /// <summary>Runs the whole test body on one STA thread - see FeatureModuleLifecycleTests for why.</summary>
    private static void RunSta(Action body)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
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
