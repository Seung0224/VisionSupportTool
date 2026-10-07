using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualPlcServer.Modules;
using VirtualPlcServer.Scenarios;

namespace VirtualPlcServer.ViewModels
{
    /// <summary>"[연산자] (상수 또는 다른 번지)" 칸 묶음. 왼쪽 번지가 문자열 노드면 숫자 대신 텍스트 칸을 보여준다.
    /// 규칙의 주 조건과 AND 조건 행이 모두 이걸 쓴다.</summary>
    public partial class ComparisonViewModel : ObservableObject
    {
        private readonly TargetPickerViewModel _left;

        public ComparisonViewModel(TargetPickerViewModel left, IReadOnlyList<PlcServerModule> availableModules)
        {
            _left = left;
            CompareTarget = new TargetPickerViewModel(availableModules);
            _left.PropertyChanged += OnLeftPropertyChanged;
        }

        /// <summary>CompareOperator enum 순서(Equals, NotEquals, GreaterThan, GreaterOrEqual, LessThan, LessOrEqual)와 일치.</summary>
        [ObservableProperty]
        private int operatorIndex;

        /// <summary>0 = 상수(Value), 1 = 다른 번지(Address).</summary>
        [ObservableProperty]
        private int compareKindIndex;

        [ObservableProperty]
        private double compareValue;

        [ObservableProperty]
        private string compareText = string.Empty;

        public TargetPickerViewModel CompareTarget { get; }

        public bool IsAddressCompare => CompareKindIndex == 1;

        public bool IsNumberValue => !IsAddressCompare && !_left.IsStringSelected;

        public bool IsTextValue => !IsAddressCompare && _left.IsStringSelected;

        public CompareOperator Operator => (CompareOperator)OperatorIndex;

        partial void OnCompareKindIndexChanged(int value)
        {
            RaiseValueKindChanged();
        }

        private void OnLeftPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TargetPickerViewModel.IsStringSelected))
            {
                RaiseValueKindChanged();
            }
        }

        private void RaiseValueKindChanged()
        {
            OnPropertyChanged(nameof(IsAddressCompare));
            OnPropertyChanged(nameof(IsNumberValue));
            OnPropertyChanged(nameof(IsTextValue));
        }

        /// <summary>문제가 없으면 null.</summary>
        public string Validate()
        {
            return IsAddressCompare && !CompareTarget.IsComplete ? "Choose the address to compare against." : null;
        }

        /// <summary>저장된 비교를 고치려고 다시 열 때 칸을 채운다. 왼쪽 번지를 먼저 채운 뒤 불러야
        /// 문자열/숫자 칸 중 어느 쪽이 보일지가 맞게 정해진다.</summary>
        public void Load(CompareOperator op, double compareValue, string compareText, TargetRef compareTarget)
        {
            OperatorIndex = (int)op;
            CompareValue = compareValue;
            CompareText = compareText ?? string.Empty;
            CompareKindIndex = compareTarget != null ? 1 : 0;
            CompareTarget.Load(compareTarget);
        }

        public TargetRef BuildCompareTarget()
        {
            return IsAddressCompare ? CompareTarget.ToTargetRef() : null;
        }
    }

    /// <summary>규칙 다이얼로그의 "AND" 행 하나.</summary>
    public partial class ScenarioConditionRowViewModel : ObservableObject
    {
        public ScenarioConditionRowViewModel(IReadOnlyList<PlcServerModule> availableModules)
        {
            Target = new TargetPickerViewModel(availableModules);
            Comparison = new ComparisonViewModel(Target, availableModules);
        }

        public TargetPickerViewModel Target { get; }

        public ComparisonViewModel Comparison { get; }

        public event EventHandler RemoveRequested;

        public void Load(ScenarioCondition condition)
        {
            Target.Load(condition.Target);
            Comparison.Load(condition.Operator, condition.CompareValue, condition.CompareText, condition.CompareTarget);
        }

        [RelayCommand]
        private void Remove()
        {
            RemoveRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>입력이 덜 됐으면 null을 돌려주고 error에 이유를 담는다.</summary>
        public ScenarioCondition TryBuild(out string error)
        {
            if (!Target.IsComplete)
            {
                error = "Choose a module and address for every AND condition.";
                return null;
            }

            error = Comparison.Validate();
            if (error != null)
            {
                return null;
            }

            var condition = new ScenarioCondition
            {
                Target = Target.ToTargetRef(),
                Operator = Comparison.Operator,
                CompareTarget = Comparison.BuildCompareTarget()
            };

            if (Comparison.IsTextValue)
            {
                condition.CompareText = Comparison.CompareText ?? string.Empty;
            }
            else if (Comparison.IsNumberValue)
            {
                condition.CompareValue = Comparison.CompareValue;
            }

            return condition;
        }
    }
}
