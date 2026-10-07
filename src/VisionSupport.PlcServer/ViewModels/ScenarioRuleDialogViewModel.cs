using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Scenarios;

namespace VirtualPlcServer.ViewModels
{
    /// <summary>"규칙 추가" 다이얼로그의 뷰모델. 트리거(값 변화/값 비교/타이머) + AND 추가 조건 + 액션(대상에 값 쓰기)을 구성한다.
    /// 번지 선택은 TargetPickerViewModel이, 비교(연산자 + 상수 또는 다른 번지)는 ComparisonViewModel이 맡는다.</summary>
    public partial class ScenarioRuleDialogViewModel : ObservableObject
    {
        public ScenarioRuleDialogViewModel(IReadOnlyList<PlcServerModule> availableModules)
        {
            AvailableModules = availableModules;
            Watch = new TargetPickerViewModel(availableModules);
            Comparison = new ComparisonViewModel(Watch, availableModules);
            Action = new TargetPickerViewModel(availableModules);
        }

        public IReadOnlyList<PlcServerModule> AvailableModules { get; }

        /// <summary>0 = On Value Change, 1 = On Value Compare, 2 = On Timer (TriggerKind enum 순서와 일치).</summary>
        [ObservableProperty]
        private int triggerKindIndex;

        public TargetPickerViewModel Watch { get; }

        public ComparisonViewModel Comparison { get; }

        [ObservableProperty]
        private double timerIntervalSeconds = 5;

        /// <summary>AND로 붙는 추가 조건 행들.</summary>
        public ObservableCollection<ScenarioConditionRowViewModel> Conditions { get; } =
            new ObservableCollection<ScenarioConditionRowViewModel>();

        public TargetPickerViewModel Action { get; }

        [ObservableProperty]
        private string actionValueExpression = "0";

        [ObservableProperty]
        private string actionTextValue = string.Empty;

        [ObservableProperty]
        private string errorMessage = string.Empty;

        public bool IsWatchNeeded => TriggerKindIndex != 2;

        public bool IsCompareNeeded => TriggerKindIndex == 1;

        public bool IsTimerNeeded => TriggerKindIndex == 2;

        /// <summary>타이머 규칙에서는 추가 조건이 "IF"(거름 조건), 그 외에는 "AND"로 읽힌다.</summary>
        public string ConditionsHeader => IsTimerNeeded ? "ONLY IF (ALL)" : "AND";

        partial void OnTriggerKindIndexChanged(int value)
        {
            OnPropertyChanged(nameof(IsWatchNeeded));
            OnPropertyChanged(nameof(IsCompareNeeded));
            OnPropertyChanged(nameof(IsTimerNeeded));
            OnPropertyChanged(nameof(ConditionsHeader));
        }

        /// <summary>기존 규칙을 고치는 중이면 true. 제목과 확인 버튼 글자만 바뀐다.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Title))]
        [NotifyPropertyChangedFor(nameof(ConfirmText))]
        private bool isEditing;

        public string Title => IsEditing ? "EDIT RULE" : "ADD RULE";

        public string ConfirmText => IsEditing ? "SAVE" : "ADD RULE";

        [RelayCommand]
        private void AddCondition()
        {
            NewConditionRow();
        }

        private ScenarioConditionRowViewModel NewConditionRow()
        {
            var row = new ScenarioConditionRowViewModel(AvailableModules);
            row.RemoveRequested += (s, e) => Conditions.Remove(row);
            Conditions.Add(row);
            return row;
        }

        /// <summary>저장된 규칙으로 모든 칸을 채운다(수정 모드). TryBuildRule은 새 규칙을 만들므로,
        /// 호출한 쪽이 원래 규칙의 Id를 이어 붙인다.</summary>
        public void LoadFrom(ScenarioRule rule)
        {
            IsEditing = true;
            TriggerKindIndex = (int)rule.Trigger;
            TimerIntervalSeconds = rule.TimerIntervalSeconds;

            Watch.Load(rule.WatchTarget);
            Comparison.Load(rule.Operator, rule.CompareValue, rule.CompareText, rule.CompareTarget);

            Conditions.Clear();
            foreach (ScenarioCondition condition in rule.Conditions ?? new List<ScenarioCondition>())
            {
                NewConditionRow().Load(condition);
            }

            Action.Load(rule.ActionTarget);
            ActionValueExpression = rule.ActionValueExpression ?? "0";
            ActionTextValue = rule.ActionTextValue ?? string.Empty;
        }

        public ScenarioRule TryBuildRule()
        {
            ErrorMessage = string.Empty;

            if (!Action.IsComplete)
            {
                ErrorMessage = "Choose an action target module and address.";
                return null;
            }

            var rule = new ScenarioRule
            {
                Trigger = (TriggerKind)TriggerKindIndex,
                ActionTarget = Action.ToTargetRef()
            };

            if (Action.IsStringSelected)
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
                if (!Watch.IsComplete)
                {
                    ErrorMessage = "Choose a watch target module and address.";
                    return null;
                }

                rule.WatchTarget = Watch.ToTargetRef();
            }

            if (rule.Trigger == TriggerKind.OnCompare)
            {
                string comparisonError = Comparison.Validate();
                if (comparisonError != null)
                {
                    ErrorMessage = comparisonError;
                    return null;
                }

                rule.Operator = Comparison.Operator;
                rule.CompareTarget = Comparison.BuildCompareTarget();
                if (Comparison.IsTextValue)
                {
                    rule.CompareText = Comparison.CompareText ?? string.Empty;
                }
                else if (Comparison.IsNumberValue)
                {
                    rule.CompareValue = Comparison.CompareValue;
                }
            }

            if (rule.Trigger == TriggerKind.OnTimer)
            {
                rule.TimerIntervalSeconds = Math.Max(0.5, TimerIntervalSeconds);
            }

            foreach (ScenarioConditionRowViewModel row in Conditions)
            {
                ScenarioCondition condition = row.TryBuild(out string conditionError);
                if (condition == null)
                {
                    ErrorMessage = conditionError;
                    return null;
                }

                rule.Conditions.Add(condition);
            }

            return rule;
        }
    }
}
