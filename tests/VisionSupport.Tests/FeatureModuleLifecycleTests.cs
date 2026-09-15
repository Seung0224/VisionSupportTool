using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using VisionSupport.Features;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Closing a feature's window must not be the same thing as stopping the feature.
///
/// A PLC hub with a VISION client mid-test has to survive its window being closed; an image
/// converter that nobody is looking at has to disappear entirely. That is one decision made in
/// two places - the window asks, the module obeys - and these tests pin the module's half.
/// </summary>
public class FeatureModuleLifecycleTests
{
    [Fact]
    public void Releasing_the_view_builds_a_fresh_one_next_time() => RunSta(() =>
    {
        var module = new FakeFeature();

        UserControl first = module.GetOrCreateView();
        module.ReleaseView();
        UserControl second = module.GetOrCreateView();

        Assert.NotSame(first, second);
        Assert.Equal(2, module.ViewsCreated);
    });

    [Fact]
    public void Releasing_the_view_does_not_stop_the_feature() => RunSta(() =>
    {
        var module = new FakeFeature();
        module.GetOrCreateView();

        module.ReleaseView();

        Assert.False(module.WasStopped);
    });

    /// <summary>
    /// Opening a window builds the feature's view and ViewModel, and nothing about that moves the
    /// state off Stopped - the tools are run from their own buttons. A Stopped feature can still be
    /// holding everything its view showed, and stopping it must let go of that.
    /// </summary>
    [Fact]
    public void Stopping_releases_everything_the_view_built() => RunSta(() =>
    {
        var module = new FakeFeature();
        UserControl first = module.GetOrCreateView();

        module.StopAsync().GetAwaiter().GetResult();

        Assert.Equal(FeatureState.Stopped, module.State);
        Assert.True(module.WasStopped);
        Assert.NotSame(first, module.GetOrCreateView());
    });

    /// <summary>
    /// Runs the whole test body on one STA thread.
    ///
    /// WPF controls need STA, and they stay owned by the thread that built them - teardown reads
    /// the view's DataContext, which is a dependency property and throws if a different thread
    /// asks for it. In the app every one of these calls is on the UI thread; the test has to be
    /// arranged the same way or it is testing an arrangement that cannot happen.
    ///
    /// The fake's operations all complete synchronously, so the awaits inside the module
    /// continue inline and never leave this thread.
    /// </summary>
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
        public int ViewsCreated { get; private set; }

        public bool WasStopped { get; private set; }

        public override string Title => "테스트";

        public override string Description => "수명주기 검증용";

        public override bool IsWorking => false;

        protected override UserControl CreateView()
        {
            ViewsCreated++;
            return new UserControl();
        }

        protected override Task OnStopAsync()
        {
            WasStopped = true;
            return Task.CompletedTask;
        }
    }
}
