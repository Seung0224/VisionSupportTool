using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using VirtualPlcServer.Core;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Protocols.Common;
using VirtualPlcServer.Scenarios;

namespace VirtualPlcServer.ViewModels
{
    /// <summary>"규칙 추가" 다이얼로그의 뷰모델. 트리거(값 변화/값 비교/타이머) + 액션(대상에 값 쓰기)을 구성한다.
    /// 대상 모듈이 OPC UA/ADS(이름 기반 노드)면 주소를 직접 타이핑하는 대신 실제 노드 목록에서 고르게 하고,
    /// 그 노드가 배열이면 원소 인덱스를, 문자열이면 텍스트 비교/쓰기 칸을 보여준다.</summary>
    public partial class ScenarioRuleDialogViewModel : ObservableObject
    {
        public ScenarioRuleDialogViewModel(IReadOnlyList<PlcServerModule> availableModules)
        {
            AvailableModules = availableModules;
        }

        public IReadOnlyList<PlcServerModule> AvailableModules { get; }

        /// <summary>0 = On Value Change, 1 = On Value Compare, 2 = On Timer (TriggerKind enum 순서와 일치).</summary>
        [ObservableProperty]
        private int triggerKindIndex;

        // ---- Watch (트리거 대상) ----

        [ObservableProperty]
        private PlcServerModule selectedWatchModule;

        /// <summary>MC 모듈일 때만 직접 입력하는 D번지. OPC UA/ADS는 SelectedWatchNode 선택 시 자동으로 채워진다.</summary>
        [ObservableProperty]
        private string watchAddress = string.Empty;

        /// <summary>OPC UA/ADS 모듈일 때, 목록에서 고른 노드.</summary>
        [ObservableProperty]
        private NodeDefinition selectedWatchNode;

        [ObservableProperty]
        private int watchArrayIndex;

        /// <summary>CompareOperator enum 순서(Equals, NotEquals, GreaterThan, GreaterOrEqual, LessThan, LessOrEqual)와 일치.</summary>
        [ObservableProperty]
        private int operatorIndex;

        [ObservableProperty]
        private double compareValue;

        [ObservableProperty]
        private string compareText = string.Empty;

        [ObservableProperty]
        private double timerIntervalSeconds = 5;

        // ---- Action (쓰기 대상) ----

        [ObservableProperty]
        private PlcServerModule selectedActionModule;

        [ObservableProperty]
        private string actionAddress = string.Empty;

        [ObservableProperty]
        private NodeDefinition selectedActionNode;

        [ObservableProperty]
        private int actionArrayIndex;

        [ObservableProperty]
        private string actionValueExpression = "0";

        [ObservableProperty]
        private string actionTextValue = string.Empty;

        [ObservableProperty]
        private string errorMessage = string.Empty;

        public bool IsWatchNeeded => TriggerKindIndex != 2;

        public bool IsCompareNeeded => TriggerKindIndex == 1;

        public bool IsTimerNeeded => TriggerKindIndex == 2;

        public bool IsWatchNodeBased => PlcTargetAccessor.IsNodeBased(SelectedWatchModule);

        public IReadOnlyList<NodeDefinition> WatchModuleNodes => PlcTargetAccessor.GetNodeDefinitions(SelectedWatchModule);

        public bool IsWatchArraySelected => SelectedWatchNode?.IsArray == true;

        public bool IsWatchStringSelected => SelectedWatchNode?.DataType == PlcDataType.String;

        public bool IsActionNodeBased => PlcTargetAccessor.IsNodeBased(SelectedActionModule);

        public IReadOnlyList<NodeDefinition> ActionModuleNodes => PlcTargetAccessor.GetNodeDefinitions(SelectedActionModule);

        public bool IsActionArraySelected => SelectedActionNode?.IsArray == true;

        public bool IsActionStringSelected => SelectedActionNode?.DataType == PlcDataType.String;

        partial void OnTriggerKindIndexChanged(int value)
        {
            OnPropertyChanged(nameof(IsWatchNeeded));
            OnPropertyChanged(nameof(IsCompareNeeded));
            OnPropertyChanged(nameof(IsTimerNeeded));
        }

        partial void OnSelectedWatchModuleChanged(PlcServerModule value)
        {
            SelectedWatchNode = null;
            WatchAddress = string.Empty;
            OnPropertyChanged(nameof(IsWatchNodeBased));
            OnPropertyChanged(nameof(WatchModuleNodes));
        }

        partial void OnSelectedWatchNodeChanged(NodeDefinition value)
        {
            WatchArrayIndex = 0;
            if (value != null)
            {
                WatchAddress = value.Name;
            }

            OnPropertyChanged(nameof(IsWatchArraySelected));
            OnPropertyChanged(nameof(IsWatchStringSelected));
        }

        partial void OnSelectedActionModuleChanged(PlcServerModule value)
        {
            SelectedActionNode = null;
            ActionAddress = string.Empty;
            OnPropertyChanged(nameof(IsActionNodeBased));
            OnPropertyChanged(nameof(ActionModuleNodes));
        }

        partial void OnSelectedActionNodeChanged(NodeDefinition value)
        {
            ActionArrayIndex = 0;
            if (value != null)
            {
                ActionAddress = value.Name;
            }

            OnPropertyChanged(nameof(IsActionArraySelected));
            OnPropertyChanged(nameof(IsActionStringSelected));
        }

        public ScenarioRule TryBuildRule()
        {
            ErrorMessage = string.Empty;

            if (SelectedActionModule == null || string.IsNullOrWhiteSpace(ActionAddress))
            {
                ErrorMessage = "Choose an action target module and address.";
                return null;
            }

            var rule = new ScenarioRule
            {
                Trigger = (TriggerKind)TriggerKindIndex,
                ActionTarget = new TargetRef
                {
                    ModuleId = SelectedActionModule.Id,
                    Address = ActionAddress.Trim(),
                    ArrayIndex = IsActionArraySelected ? ActionArrayIndex : (int?)null
                }
            };

            if (IsActionStringSelected)
            {
                rule.ActionTextValue = ActionTextValue ?? string.Empty;
            }
            else
            {
                rule.ActionValueExpression = string.IsNullOrWhiteSpace(ActionValueExpression) ? "0" : ActionValueExpression.Trim();
                try
                {
                    ExpressionEvaluator.Evaluate(rule.ActionValueExpression, 0);
                }
                catch (Exception ex)
                {
                    ErrorMessage = "Invalid action value/expression: " + ex.Message;
                    return null;
                }
            }

            if (rule.Trigger != TriggerKind.OnTimer)
            {
                if (SelectedWatchModule == null || string.IsNullOrWhiteSpace(WatchAddress))
                {
                    ErrorMessage = "Choose a watch target module and address.";
                    return null;
                }

                rule.WatchTarget = new TargetRef
                {
                    ModuleId = SelectedWatchModule.Id,
                    Address = WatchAddress.Trim(),
                    ArrayIndex = IsWatchArraySelected ? WatchArrayIndex : (int?)null
                };
            }

            if (rule.Trigger == TriggerKind.OnCompare)
            {
                rule.Operator = (CompareOperator)OperatorIndex;
                if (IsWatchStringSelected)
                {
                    rule.CompareText = CompareText ?? string.Empty;
                }
                else
                {
                    rule.CompareValue = CompareValue;
                }
            }

            if (rule.Trigger == TriggerKind.OnTimer)
            {
                rule.TimerIntervalSeconds = Math.Max(0.5, TimerIntervalSeconds);
            }

            return rule;
        }
    }
}
