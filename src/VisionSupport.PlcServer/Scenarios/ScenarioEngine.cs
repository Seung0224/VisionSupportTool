using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using VirtualPlcServer.Core;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Protocols.Common;
using VirtualPlcServer.Protocols.Mc;

namespace VirtualPlcServer.Scenarios
{
    /// <summary>
    /// 활성화된 시나리오들의 규칙을 실제로 실행하는 엔진. OnChange/OnCompare 규칙은 대상 모듈의
    /// Map.ValueChanged 이벤트를 구독해서 반응하고, OnTimer 규칙은 공용 타이머 하나로 처리한다.
    /// OnCompare 규칙은 AND 추가 조건의 왼쪽 번지도 구독한다 - 카메라 같은 상대가 여러 워드를 어떤
    /// 순서로 쓰든, 마지막 하나가 맞춰지는 순간 실행되게 하기 위해서다. 반면 비교 대상 번지(오른쪽,
    /// CompareTarget)는 구독하지 않는다: 이쪽이 새 커맨드를 쓴 순간 상대가 아직 이전 사이클의 ACK를
    /// 들고 있으면, 그 쓰기만으로 규칙이 실행돼 커맨드가 곧바로 지워지기 때문이다.
    /// </summary>
    public sealed class ScenarioEngine : IDisposable
    {
        private const int MaxChainDepth = 20;

        private readonly Func<IEnumerable<PlcServerModule>> _plcModulesProvider;
        private readonly List<ScenarioModule> _activeScenarios = new List<ScenarioModule>();
        private readonly List<(IPlcMap Map, EventHandler<MapValueChangedEventArgs> Handler)> _subscriptions =
            new List<(IPlcMap, EventHandler<MapValueChangedEventArgs>)>();
        private readonly DispatcherTimer _timer;

        [ThreadStatic]
        private static int _chainDepth;

        public ScenarioEngine(Func<IEnumerable<PlcServerModule>> plcModulesProvider)
        {
            _plcModulesProvider = plcModulesProvider;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        public void Activate(ScenarioModule scenario)
        {
            if (!_activeScenarios.Contains(scenario))
            {
                _activeScenarios.Add(scenario);
                foreach (ScenarioRule rule in scenario.Rules)
                {
                    rule.ElapsedSeconds = 0;
                }
            }

            Resync();
        }

        public void Deactivate(ScenarioModule scenario)
        {
            _activeScenarios.Remove(scenario);
            Resync();
        }

        /// <summary>보드에 모듈이 추가/삭제될 때마다 호출해서 구독을 다시 맞춘다.</summary>
        public void NotifyModulesChanged()
        {
            Resync();
        }

        /// <summary>
        /// 타이머를 멈추고 맵 구독을 푼다. 켜진 DispatcherTimer는 Dispatcher가 붙잡고 있어서, 멈추지 않으면
        /// 엔진과 엔진이 가리키는 보드(모든 모듈)가 창을 닫은 뒤에도 GC되지 않는다.
        /// </summary>
        public void Dispose()
        {
            _timer.Stop();
            _activeScenarios.Clear();
            Resync();
        }

        private void Resync()
        {
            foreach ((IPlcMap map, EventHandler<MapValueChangedEventArgs> handler) in _subscriptions)
            {
                map.ValueChanged -= handler;
            }

            _subscriptions.Clear();

            Dictionary<Guid, PlcServerModule> modules = _plcModulesProvider().ToDictionary(m => m.Id);

            foreach (ScenarioModule scenario in _activeScenarios)
            {
                foreach (ScenarioRule rule in scenario.Rules)
                {
                    if (rule.Trigger == TriggerKind.OnTimer || rule.WatchTarget == null)
                    {
                        continue;
                    }

                    Subscribe(modules, rule, rule.WatchTarget);

                    if (rule.Trigger == TriggerKind.OnCompare && rule.Conditions != null)
                    {
                        foreach (ScenarioCondition condition in rule.Conditions)
                        {
                            if (condition?.Target != null)
                            {
                                Subscribe(modules, rule, condition.Target);
                            }
                        }
                    }
                }
            }
        }

        private void Subscribe(Dictionary<Guid, PlcServerModule> modules, ScenarioRule rule, TargetRef watched)
        {
            if (!modules.TryGetValue(watched.ModuleId, out PlcServerModule watchModule))
            {
                return;
            }

            EventHandler<MapValueChangedEventArgs> handler = (s, e) => OnValueChanged(watchModule, watched, rule, e);
            watchModule.Server.Map.ValueChanged += handler;
            _subscriptions.Add((watchModule.Server.Map, handler));
        }

        /// <summary>watched는 규칙의 주 감시 대상이거나 AND 추가 조건 하나의 왼쪽 번지다.</summary>
        private void OnValueChanged(PlcServerModule watchModule, TargetRef watched, ScenarioRule rule, MapValueChangedEventArgs e)
        {
            if (!AddressMatches(watchModule, watched.Address, e.Key))
            {
                return;
            }

            // 주 감시 번지에서 온 이벤트면 이벤트 값을(배열 노드면 그 원소만), 추가 조건 번지에서 온 것이면
            // 주 감시 번지의 현재 값을 읽어 주 조건을 평가한다.
            bool fromWatchTarget = ReferenceEquals(watched, rule.WatchTarget);
            object rawValue = fromWatchTarget
                ? ExtractElement(e.Value, rule.WatchTarget.ArrayIndex)
                : ConditionEvaluator.Read(rule.WatchTarget, FindModule);

            if (rawValue == null)
            {
                return;
            }

            bool conditionMet = rule.Trigger == TriggerKind.OnChange
                ? fromWatchTarget
                : ConditionEvaluator.Compare(rawValue, rule.Operator, rule.CompareValue, rule.CompareText,
                    rule.CompareTarget, FindModule);

            if (!conditionMet || !ConditionEvaluator.AllMet(rule.Conditions, FindModule))
            {
                return;
            }

            Fire(rule, PlcTargetAccessor.ToDouble(rawValue));
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            foreach (ScenarioModule scenario in _activeScenarios.ToList())
            {
                foreach (ScenarioRule rule in scenario.Rules)
                {
                    if (rule.Trigger != TriggerKind.OnTimer)
                    {
                        continue;
                    }

                    rule.ElapsedSeconds += _timer.Interval.TotalSeconds;
                    if (rule.ElapsedSeconds < rule.TimerIntervalSeconds)
                    {
                        continue;
                    }

                    rule.ElapsedSeconds = 0;
                    if (ConditionEvaluator.AllMet(rule.Conditions, FindModule))
                    {
                        Fire(rule, 0);
                    }
                }
            }
        }

        private void Fire(ScenarioRule rule, double triggerValue)
        {
            if (rule.ActionTarget == null || _chainDepth >= MaxChainDepth)
            {
                return;
            }

            PlcServerModule actionModule = FindModule(rule.ActionTarget.ModuleId);
            if (actionModule == null)
            {
                return;
            }

            bool actionIsString = PlcTargetAccessor.TryGetNodeDefinition(actionModule, rule.ActionTarget.Address, out NodeDefinition actionNode)
                && actionNode.DataType == PlcDataType.String;

            object valueToWrite;
            if (actionIsString)
            {
                valueToWrite = rule.ActionTextValue ?? string.Empty;
            }
            else
            {
                try
                {
                    valueToWrite = ExpressionEvaluator.Evaluate(rule.ActionValueExpression, triggerValue);
                }
                catch
                {
                    return;
                }
            }

            _chainDepth++;
            try
            {
                PlcTargetAccessor.WriteRaw(actionModule, rule.ActionTarget, valueToWrite);
            }
            finally
            {
                _chainDepth--;
            }
        }

        private PlcServerModule FindModule(Guid id)
        {
            return _plcModulesProvider().FirstOrDefault(m => m.Id == id);
        }

        private static object ExtractElement(object value, int? index)
        {
            if (index.HasValue && value is Array array && index.Value >= 0 && index.Value < array.Length)
            {
                return array.GetValue(index.Value);
            }

            return value;
        }

        private static bool AddressMatches(PlcServerModule module, string configuredAddress, string eventKey)
        {
            if (module.Server is McPlcServer)
            {
                int configured = PlcTargetAccessor.ParseMcAddress(configuredAddress);
                return string.Equals(eventKey, "D" + configured, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(eventKey, (configuredAddress ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
