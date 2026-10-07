using System.Net;
using System.Runtime.ExceptionServices;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Protocols.Mc;
using VirtualPlcServer.Scenarios;
using VirtualPlcServer.ViewModels;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// Editing a module or a rule reopens the dialog that made it, filled in. Scenario rules point at a
/// PLC module by Id, so an edited PLC module has to come back as the same Id or every rule that
/// watches it silently stops working.
/// </summary>
public class ModuleEditTests
{
    [Fact]
    public void An_edited_plc_module_keeps_its_id_and_its_values() => OnSta(() =>
    {
        var original = NewMcModule("PLC SERVER", "192.168.130.1");
        ((McPlcServer)original.Server).McMap.WriteWord(30036, 1011);

        var dialog = AddModuleDialogViewModel.ForEdit(original, null!, () => new[] { original });
        dialog.McListenIp = "192.168.130.157";
        dialog.ModuleName = "PLC SERVER 2";

        var edited = Assert.IsType<PlcServerModule>(dialog.TryBuildModule());

        Assert.NotSame(original, edited);
        Assert.Equal(original.Id, edited.Id);
        Assert.Equal(original.CreatedAt, edited.CreatedAt);
        Assert.Equal("PLC SERVER 2", edited.Name);
        var server = Assert.IsType<McPlcServer>(edited.Server);
        Assert.Equal(IPAddress.Parse("192.168.130.157"), server.Config.ListenAddress);
        Assert.Equal(1011, server.McMap.ReadWord(30036));
    });

    [Fact]
    public void The_edit_dialog_starts_from_the_module_settings() => OnSta(() =>
    {
        var original = NewMcModule("PLC SERVER", "192.168.130.1");

        var dialog = AddModuleDialogViewModel.ForEdit(original, null!, () => new[] { original });

        Assert.True(dialog.IsEditing);
        Assert.True(dialog.TypeChosen);
        Assert.True(dialog.IsPlcServerSelected);
        Assert.Equal(0, dialog.ProtocolIndex);
        Assert.Equal("192.168.130.1", dialog.McListenIp);
        Assert.Equal(9001, dialog.McReadPort);
        Assert.Equal(9002, dialog.McWritePort);
        Assert.Equal(30000, dialog.McStartAddress);
        Assert.Equal(100, dialog.McSize);
    });

    [Fact]
    public void An_any_address_shows_as_blank() => OnSta(() =>
    {
        var original = NewMcModule("PLC SERVER", null);

        var dialog = AddModuleDialogViewModel.ForEdit(original, null!, () => new[] { original });

        Assert.Equal(string.Empty, dialog.McListenIp);
    });

    [Fact]
    public void Renaming_a_scenario_keeps_the_same_module_and_its_rules() => OnSta(() =>
    {
        var engine = new ScenarioEngine(Array.Empty<PlcServerModule>);
        try
        {
            var rule = new ScenarioRule();
            var scenario = new ScenarioModule(engine, Array.Empty<PlcServerModule>, "HIK Auto Cmd", new List<ScenarioRule> { rule });

            var dialog = AddModuleDialogViewModel.ForEdit(scenario, engine, Array.Empty<PlcServerModule>);
            dialog.ModuleName = "HIK Auto Cmd 1011";

            Assert.Same(scenario, dialog.TryBuildModule());
            Assert.Equal("HIK Auto Cmd 1011", scenario.Name);
            Assert.Same(rule, Assert.Single(scenario.Rules));
        }
        finally
        {
            engine.Dispose();
        }
    });

    [Fact]
    public void A_rule_reopened_for_editing_builds_back_the_same_rule() => OnSta(() =>
    {
        var plc = NewMcModule("PLC", null);
        TargetRef at(int address) => new() { ModuleId = plc.Id, Address = address.ToString() };
        var rule = new ScenarioRule
        {
            Trigger = TriggerKind.OnCompare,
            WatchTarget = at(30028),
            Operator = CompareOperator.Equals,
            CompareTarget = at(30036),
            Conditions = { new ScenarioCondition { Target = at(30029), Operator = CompareOperator.GreaterOrEqual, CompareValue = 1 } },
            ActionTarget = at(30036),
            ActionValueExpression = "value - 1011"
        };

        var dialog = new ScenarioRuleDialogViewModel(new[] { plc });
        dialog.LoadFrom(rule);
        ScenarioRule? rebuilt = dialog.TryBuildRule();

        Assert.True(dialog.IsEditing);
        Assert.NotNull(rebuilt);
        Assert.Equal(rule.Describe(_ => "PLC"), rebuilt!.Describe(_ => "PLC"));
    });

    [Fact]
    public void A_timer_rule_reopened_for_editing_keeps_its_interval() => OnSta(() =>
    {
        var plc = NewMcModule("PLC", null);
        var rule = new ScenarioRule
        {
            Trigger = TriggerKind.OnTimer,
            TimerIntervalSeconds = 10,
            Conditions = { new ScenarioCondition { Target = new TargetRef { ModuleId = plc.Id, Address = "30036" }, CompareValue = 0 } },
            ActionTarget = new TargetRef { ModuleId = plc.Id, Address = "30036" },
            ActionValueExpression = "1011"
        };

        var dialog = new ScenarioRuleDialogViewModel(new[] { plc });
        dialog.LoadFrom(rule);

        Assert.Equal(rule.Describe(_ => "PLC"), dialog.TryBuildRule()!.Describe(_ => "PLC"));
    });

    private static PlcServerModule NewMcModule(string name, string? ip) => new(
        new McPlcServer(new McServerConfig
        {
            ListenAddress = ip == null ? IPAddress.Any : IPAddress.Parse(ip),
            ReadPort = 9001,
            WritePort = 9002,
            StartAddress = 30000,
            Size = 100
        }),
        name);

    private static void OnSta(Action body)
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
