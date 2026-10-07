using System.Runtime.ExceptionServices;
using Newtonsoft.Json;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Protocols.Mc;
using VirtualPlcServer.Scenarios;
using VirtualPlcServer.ViewModels;
using Xunit;

namespace VisionSupport.Tests;

/// <summary>
/// The Hikrobot smart camera handshake, with this tool as the PLC. The camera writes D30026 STATUS,
/// D30028 COMMAND ACK and D30029 VISION PROCESSING; the PLC side writes only D30036 COMMAND and
/// D30038 STATUS ACK. Writes go straight into the MC map - the same WriteWord the socket path calls.
/// </summary>
public class ScenarioConditionTests
{
    private const int Status = 30026, CommandAck = 30028, Processing = 30029, Command = 30036, StatusAck = 30038;

    [Fact]
    public void The_command_clears_only_once_the_ack_matches_and_vision_is_processing() => Run(board =>
    {
        board.Add(ClearCommandOnAck(board));
        board.Write(Command, 1011);

        board.Write(CommandAck, 1011);
        Assert.Equal(1011, board.Read(Command));

        board.Write(Processing, 1);
        Assert.Equal(0, board.Read(Command));
    });

    [Fact]
    public void The_order_the_camera_writes_its_words_in_does_not_matter() => Run(board =>
    {
        board.Add(ClearCommandOnAck(board));
        board.Write(Command, 1011);

        board.Write(Processing, 1);
        Assert.Equal(1011, board.Read(Command));

        board.Write(CommandAck, 1011);
        Assert.Equal(0, board.Read(Command));
    });

    [Fact]
    public void An_ack_for_a_different_command_does_not_clear() => Run(board =>
    {
        board.Add(ClearCommandOnAck(board));
        board.Write(Command, 1011);

        board.Write(Processing, 1);
        board.Write(CommandAck, 1012);

        Assert.Equal(1011, board.Read(Command));
    });

    /// <summary>The compared-to address is read, never watched: a new command written while the
    /// camera still holds the previous cycle's ack must not be cleared on the spot.</summary>
    [Fact]
    public void Writing_a_new_command_over_a_stale_ack_does_not_clear_it() => Run(board =>
    {
        board.Write(CommandAck, 1011);
        board.Write(Processing, 1);
        board.Add(ClearCommandOnAck(board));

        board.Write(Command, 1011);

        Assert.Equal(1011, board.Read(Command));
    });

    [Fact]
    public void Status_is_echoed_and_cleared_once_status_and_processing_are_both_zero() => Run(board =>
    {
        board.Add(new ScenarioRule
        {
            Trigger = TriggerKind.OnCompare,
            WatchTarget = board.At(Status),
            Operator = CompareOperator.NotEquals,
            CompareValue = 0,
            ActionTarget = board.At(StatusAck),
            ActionValueExpression = "value"
        });
        board.Add(new ScenarioRule
        {
            Trigger = TriggerKind.OnCompare,
            WatchTarget = board.At(Status),
            Operator = CompareOperator.Equals,
            CompareValue = 0,
            Conditions = { new ScenarioCondition { Target = board.At(Processing), Operator = CompareOperator.Equals, CompareValue = 0 } },
            ActionTarget = board.At(StatusAck),
            ActionValueExpression = "0"
        });

        board.Write(Processing, 1);
        board.Write(Status, 7);
        Assert.Equal(7, board.Read(StatusAck));

        board.Write(Status, 0);
        Assert.Equal(7, board.Read(StatusAck));

        board.Write(Processing, 0);
        Assert.Equal(0, board.Read(StatusAck));
    });

    [Fact]
    public void A_condition_on_an_unreadable_address_is_false() => Run(board =>
    {
        var condition = new ScenarioCondition { Target = board.At(99999), Operator = CompareOperator.Equals, CompareValue = 0 };

        Assert.False(ConditionEvaluator.IsMet(condition, board.Lookup));
    });

    [Fact]
    public void Conditions_compare_against_another_address() => Run(board =>
    {
        var condition = new ScenarioCondition
        {
            Target = board.At(CommandAck),
            Operator = CompareOperator.Equals,
            CompareTarget = board.At(Command)
        };

        board.Write(Command, 1011);
        board.Write(CommandAck, 1011);
        Assert.True(ConditionEvaluator.IsMet(condition, board.Lookup));

        board.Write(CommandAck, 1012);
        Assert.False(ConditionEvaluator.IsMet(condition, board.Lookup));
    });

    [Fact]
    public void A_rule_saved_before_conditions_existed_loads_with_none() => Run(board =>
    {
        const string oldJson = "{\"Trigger\":1,\"Operator\":0,\"CompareValue\":5,\"ActionValueExpression\":\"0\"}";

        var rule = JsonConvert.DeserializeObject<ScenarioRule>(oldJson)!;

        Assert.Empty(rule.Conditions);
        Assert.Null(rule.CompareTarget);
    });

    [Fact]
    public void The_description_reads_as_one_line() => Run(board =>
    {
        string text = ClearCommandOnAck(board).Describe(_ => "PLC");

        Assert.Equal("WHEN PLC.30028 == PLC.30036 AND PLC.30029 == 1  →  WRITE 0 TO PLC.30036", text);
    });

    [Fact]
    public void The_rule_dialog_builds_the_ack_rule_from_its_fields() => Run(board =>
    {
        var dialog = new ScenarioRuleDialogViewModel(new[] { board.Plc }) { TriggerKindIndex = 1, ActionValueExpression = "0" };
        dialog.Watch.Address = "30028";
        dialog.Comparison.CompareKindIndex = 1;
        dialog.Comparison.CompareTarget.Address = "30036";
        dialog.AddConditionCommand.Execute(null);
        dialog.Conditions[0].Target.Address = "30029";
        dialog.Conditions[0].Comparison.CompareValue = 1;
        dialog.Action.Address = "30036";

        ScenarioRule? rule = dialog.TryBuildRule();

        Assert.NotNull(rule);
        Assert.Equal(ClearCommandOnAck(board).Describe(_ => "PLC"), rule!.Describe(_ => "PLC"));
    });

    [Fact]
    public void The_rule_dialog_refuses_a_condition_without_an_address() => Run(board =>
    {
        var dialog = new ScenarioRuleDialogViewModel(new[] { board.Plc }) { TriggerKindIndex = 1 };
        dialog.Watch.Address = "30028";
        dialog.Action.Address = "30036";
        dialog.AddConditionCommand.Execute(null);

        Assert.Null(dialog.TryBuildRule());
        Assert.NotEmpty(dialog.ErrorMessage);
    });

    /// <summary>The engine builds its map subscriptions from the rule list, so a rule removed from an
    /// active scenario has to stop firing right away - not only after the checkbox is toggled.</summary>
    [Fact]
    public void A_rule_removed_from_an_active_scenario_stops_firing() => Run(board =>
    {
        ScenarioRule rule = ClearCommandOnAck(board);
        board.Add(rule);
        board.Remove(rule);
        board.Write(Command, 1011);

        board.Write(CommandAck, 1011);
        board.Write(Processing, 1);

        Assert.Equal(1011, board.Read(Command));
    });

    [Fact]
    public void A_rule_added_to_an_active_scenario_fires_without_a_restart() => Run(board =>
    {
        board.Add(new ScenarioRule());
        board.AddWhileActive(ClearCommandOnAck(board));
        board.Write(Command, 1011);

        board.Write(CommandAck, 1011);
        board.Write(Processing, 1);

        Assert.Equal(0, board.Read(Command));
    });

    private static ScenarioRule ClearCommandOnAck(Board board) => new()
    {
        Trigger = TriggerKind.OnCompare,
        WatchTarget = board.At(CommandAck),
        Operator = CompareOperator.Equals,
        CompareTarget = board.At(Command),
        Conditions = { new ScenarioCondition { Target = board.At(Processing), Operator = CompareOperator.Equals, CompareValue = 1 } },
        ActionTarget = board.At(Command),
        ActionValueExpression = "0"
    };

    private sealed class Board
    {
        private readonly PlcServerModule _plc;

        public PlcServerModule Plc => _plc;
        private readonly ScenarioEngine _engine;
        private readonly ScenarioModule _scenario;

        public Board()
        {
            _plc = new PlcServerModule(new McPlcServer(new McServerConfig { StartAddress = 30000, Size = 100 }), "PLC");
            _engine = new ScenarioEngine(() => new[] { _plc });
            _scenario = new ScenarioModule(_engine, () => new[] { _plc }, "Handshake", new List<ScenarioRule>());
        }

        private McMap Map => ((McPlcServer)_plc.Server).McMap;

        public PlcServerModule? Lookup(Guid id) => id == _plc.Id ? _plc : null;

        public TargetRef At(int address) => new() { ModuleId = _plc.Id, Address = address.ToString() };

        public void Add(ScenarioRule rule)
        {
            _scenario.Rules.Add(rule);
            _scenario.StartAsync();
        }

        public void Remove(ScenarioRule rule)
        {
            _scenario.Rules.Remove(rule);
            _scenario.NotifyRulesChanged();
        }

        /// <summary>What the editor window does: change the list, then notify - no restart.</summary>
        public void AddWhileActive(ScenarioRule rule)
        {
            _scenario.Rules.Add(rule);
            _scenario.NotifyRulesChanged();
        }

        public void Write(int address, short value) => Map.WriteWord(address, unchecked((ushort)value));

        public short Read(int address) => unchecked((short)Map.ReadWord(address));

        public void Dispose() => _engine.Dispose();
    }

    /// <summary>The engine's DispatcherTimer belongs to the thread that builds it - STA, as in the app.</summary>
    private static void Run(Action<Board> body)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            var board = new Board();
            try { body(board); }
            catch (Exception ex) { failure = ex; }
            finally { board.Dispose(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
