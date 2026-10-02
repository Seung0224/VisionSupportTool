using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// One shared, persistent STA thread with its own <see cref="System.Windows.Application"/>, for
/// tests that show and close real windows.
///
/// <see cref="Application"/> is a true one-per-process singleton: a second <c>new Application()</c>
/// call anywhere in the process throws, even from a fresh thread, even after the first thread that
/// created one has exited. Automation-disconnect tests each need their own STA thread to pump a
/// dispatcher on, so they cannot each create their own Application - they have to share exactly
/// one, which is what this fixture is for.
/// </summary>
public sealed class StaAppFixture : IDisposable
{
    private readonly Thread _thread;

    public Dispatcher Dispatcher { get; }

    public StaAppFixture()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;

        _thread = new Thread(() =>
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        });
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Start();
        ready.Wait();

        Dispatcher = dispatcher!;
    }

    /// <summary>Runs <paramref name="body"/> on the shared STA thread, re-throwing any failure on
    /// the calling (test) thread so xunit reports it against the right test.</summary>
    public void Run(Action body)
    {
        Dispatcher.Invoke(body);
    }

    public void Dispose()
    {
        Dispatcher.InvokeShutdown();
        _thread.Join();
    }
}

[CollectionDefinition(Name)]
public sealed class StaAppCollection : ICollectionFixture<StaAppFixture>
{
    public const string Name = "STA WPF Application";
}
