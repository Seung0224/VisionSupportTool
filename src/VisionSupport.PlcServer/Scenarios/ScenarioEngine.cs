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

                    if (!modules.TryGetValue(rule.WatchTarget.ModuleId, out PlcServerModule watchModule))
                    {
                        continue;
                    }

                    ScenarioRule capturedRule = rule;
                    PlcServerModule capturedModule = watchModule;
                    EventHandler<MapValueChangedEventArgs> handler = (s, e) => OnValueChanged(capturedModule, capturedRule, e);
                    watchModule.Server.Map.ValueChanged += handler;
                    _subscriptions.Add((watchModule.Server.Map, handler));
                }
            }
        }

        private void OnValueChanged(PlcServerModule watchModule, ScenarioRule rule, MapValueChangedEventArgs e)
        {
            if (!AddressMatches(watchModule, rule.WatchTarget.Address, e.Key))
            {
                return;
            }

            // 배열 노드 전체가 바뀐 이벤트에서, 이 규칙이 특정 원소만 보고 있다면 그 원소만 꺼낸다.
            object rawValue = ExtractElement(e.Value, rule.WatchTarget.ArrayIndex);

            bool conditionMet;
            if (rule.Trigger == TriggerKind.OnChange)
            {
                conditionMet = true;
            }
            else if (rawValue is string text)
            {
                conditionMet = CompareText(text, rule.Operator, rule.CompareText ?? string.Empty);
            }
            else
            {
                conditionMet = CompareNumeric(PlcTargetAccessor.ToDouble(rawValue), rule.Operator, rule.CompareValue);
            }

            if (!conditionMet)
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
                    Fire(rule, 0);
                }
            }
        }

        private void Fire(ScenarioRule rule, double triggerValue)
        {
            if (rule.ActionTarget == null || _chainDepth >= MaxChainDepth)
            {
                return;
            }

            PlcServerModule actionModule = _plcModulesProvider().FirstOrDefault(m => m.Id == rule.ActionTarget.ModuleId);
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

        private static bool CompareNumeric(double value, CompareOperator op, double compareValue)
        {
            switch (op)
            {
                case CompareOperator.Equals: return Math.Abs(value - compareValue) < 0.0001;
                case CompareOperator.NotEquals: return Math.Abs(value - compareValue) >= 0.0001;
                case CompareOperator.GreaterThan: return value > compareValue;
                case CompareOperator.GreaterOrEqual: return value >= compareValue;
                case CompareOperator.LessThan: return value < compareValue;
                case CompareOperator.LessOrEqual: return value <= compareValue;
                default: return false;
            }
        }

        private static bool CompareText(string value, CompareOperator op, string compareValue)
        {
            int cmp = string.CompareOrdinal(value ?? string.Empty, compareValue ?? string.Empty);
            switch (op)
            {
                case CompareOperator.Equals: return cmp == 0;
                case CompareOperator.NotEquals: return cmp != 0;
                case CompareOperator.GreaterThan: return cmp > 0;
                case CompareOperator.GreaterOrEqual: return cmp >= 0;
                case CompareOperator.LessThan: return cmp < 0;
                case CompareOperator.LessOrEqual: return cmp <= 0;
                default: return false;
            }
        }
    }
}
