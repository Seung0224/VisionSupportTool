using System.Windows;
using VirtualPlcServer.Core;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Protocols.Common;
using VirtualPlcServer.Protocols.Mc;
using VirtualPlcServer.Protocols.OpcUa;
using VirtualPlcServer.Views;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Every view has to be constructible on its own, with nothing in Application.Resources.
///
/// This is the invariant that broke when the PLC feature's Material theme moved out of
/// Application.Resources and into the feature's own views. A dialog is built with `new` before
/// it is attached to anything, and StaticResource is resolved at that moment by walking the
/// logical tree - which, for a detached control, ends at Application.Resources. Any theme brush
/// or style the dialog names therefore has to be reachable from the dialog's own Resources.
///
/// Constructing with no Application at all is the strictest version of that check: it fails for
/// exactly the dialogs that were relying on someone else having merged the theme for them.
/// </summary>
public class ViewConstructionTests
{
    [Fact]
    public void AddModuleDialog_constructs_detached() => OnStaThread(() => new AddModuleDialog());

    [Fact]
    public void AddNodeDialog_constructs_detached() => OnStaThread(() => new AddNodeDialog());

    [Fact]
    public void ScenarioRuleDialog_constructs_detached() => OnStaThread(() => new ScenarioRuleDialog());

    [Fact]
    public void NodeValueDialog_constructs_detached() => OnStaThread(
        () => new NodeValueDialog(new NodeDefinition("n", PlcDataType.Int32, false, 1, 0)));

    [Fact]
    public void WriteValueDialog_constructs_detached() => OnStaThread(
        () => new WriteValueDialog("D100", 0));

    [Fact]
    public void NodeMonitorView_constructs_detached() => OnStaThread(
        () => new NodeMonitorView(new NodeMap(), "test"));

    [Fact]
    public void McMonitorView_constructs_detached() => OnStaThread(
        () => new McMonitorView(new McMap(0, 10), "test"));

    [Fact]
    public void PlcMonitorWindow_constructs_detached() => OnStaThread(() =>
    {
        var server = new OpcUaPlcServer(new OpcUaServerConfig { Port = 48411, ApplicationName = "t" });
        return new PlcMonitorWindow(new PlcServerModule(server, "t"));
    });

    // ImageConverterView is not constructed here: like the memory monitor's views it reads the
    // shell's Theme/Dark.xaml through StaticResource, so it is only whole inside the shell. XAML
    // compilation at build time covers "does it parse".

    /// <summary>
    /// WPF needs an STA thread, and a control built on a thread that never had an Application is
    /// exactly the "nothing global to fall back on" case being asserted. Exceptions are carried
    /// back so the test reports the XAML failure rather than a thread that quietly died.
    /// </summary>
    private static void OnStaThread(Func<object> build)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                build();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            Assert.Fail(Describe(failure));
        }
    }

    /// <summary>Surfaces the missing resource key, which is buried under the XamlParseException.</summary>
    private static string Describe(Exception failure)
    {
        var lines = new List<string> { failure.GetType().Name + ": " + failure.Message };
        for (Exception? inner = failure.InnerException; inner is not null; inner = inner.InnerException)
        {
            lines.Add("  ---> " + inner.GetType().Name + ": " + inner.Message);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
