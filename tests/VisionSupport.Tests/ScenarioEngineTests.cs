using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Scenarios;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The engine runs a DispatcherTimer from the moment it is built, and a running DispatcherTimer is
/// held by the Dispatcher. Anything the engine can reach - through its module provider, the whole
/// PLC board - stays alive with it. Releasing the PLC feature only frees memory if a disposed
/// engine can actually be collected.
/// </summary>
public class ScenarioEngineTests
{
    [Fact]
    public void A_disposed_engine_can_be_collected() => RunSta(() =>
    {
        WeakReference engine = CreateAndDispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(engine.IsAlive);
    });

    /// <summary>Separate and not inlined, so no local in the test body keeps the engine reachable.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndDispose()
    {
        var engine = new ScenarioEngine(() => Array.Empty<PlcServerModule>());
        engine.Dispose();
        return new WeakReference(engine);
    }

    /// <summary>The timer belongs to the Dispatcher of the thread that built the engine, and the
    /// app builds it on the STA UI thread - the test has to do the same.</summary>
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
}
