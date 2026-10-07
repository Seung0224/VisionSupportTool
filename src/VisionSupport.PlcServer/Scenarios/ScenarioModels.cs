using System;
using System.Collections.Generic;
using System.Linq;

namespace VirtualPlcServer.Scenarios
{
    public enum TriggerKind
    {
        /// <summary>감시 대상 주소의 값이 바뀔 때마다(값과 무관하게) 실행.</summary>
        OnChange,

        /// <summary>감시 대상 주소의 값이 바뀔 때, 그 값이 조건(연산자+비교값)을 만족하면 실행.</summary>
        OnCompare,

        /// <summary>일정 주기(초)마다 실행. 감시 대상이 없다.</summary>
        OnTimer
    }

    public enum CompareOperator
    {
        Equals,
        NotEquals,
        GreaterThan,
        GreaterOrEqual,
        LessThan,
        LessOrEqual
    }

    /// <summary>현재 보드에 있는 PLC 모듈 중 하나 + 그 안의 주소(MC는 D번지 숫자, OPC UA/ADS는 노드 이름) +
    /// 그 주소가 배열 노드일 때 어느 원소를 가리키는지(ArrayIndex, 배열이 아니면 null).</summary>
    public sealed class TargetRef
    {
        public Guid ModuleId { get; set; }

        public string Address { get; set; } = string.Empty;

        public int? ArrayIndex { get; set; }

        public string DescribeAddress()
        {
            return ArrayIndex.HasValue ? Address + "[" + ArrayIndex.Value + "]" : Address;
        }
    }

    /// <summary>
    /// 규칙에 AND로 붙는 추가 조건 하나. "왼쪽 번지의 현재 값 [연산자] (상수 또는 다른 번지의 현재 값)".
    /// 왼쪽 번지(Target)에 값이 쓰이면 규칙 전체를 다시 평가하지만, 비교 대상 번지(CompareTarget)는
    /// 평가할 때 읽기만 하고 그 번지의 변경으로는 실행하지 않는다 - 자세한 이유는 ScenarioEngine 참고.
    /// </summary>
    public sealed class ScenarioCondition
    {
        public TargetRef Target { get; set; }

        public CompareOperator Operator { get; set; } = CompareOperator.Equals;

        /// <summary>CompareTarget이 null이고 값이 숫자/불리언일 때 비교할 상수.</summary>
        public double CompareValue { get; set; }

        /// <summary>CompareTarget이 null이고 값이 문자열일 때 비교할 상수.</summary>
        public string CompareText { get; set; } = string.Empty;

        /// <summary>null이 아니면 상수 대신 이 번지의 현재 값과 비교한다.</summary>
        public TargetRef CompareTarget { get; set; }

        public string Describe(Func<Guid, string> moduleNameLookup)
        {
            return ScenarioRule.DescribeComparison(moduleNameLookup, Target, Operator, CompareValue, CompareText, CompareTarget);
        }
    }

    /// <summary>
    /// 시나리오를 구성하는 규칙 하나. "조건(Trigger)이 맞으면 행동(Action)을 한다"는 한 줄짜리 룰.
    /// 여러 개를 모으면 값이 연쇄적으로 움직이는 시나리오가 된다(예: A에 1033이 쓰이면 B에 5를 쓰고,
    /// C에 1033이 들어오면 A를 0으로 되돌리는 것 = 두 개의 ScenarioRule).
    /// 값은 대상 노드가 문자열이면 CompareText/ActionTextValue를, 숫자/불리언이면 CompareValue/
    /// ActionValueExpression을 쓴다 - 어느 쪽인지는 실행 시점에 실제 값의 타입을 보고 엔진이 판단한다.
    /// </summary>
    public sealed class ScenarioRule
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public TriggerKind Trigger { get; set; } = TriggerKind.OnCompare;

        /// <summary>OnChange/OnCompare일 때만 사용. OnTimer는 감시 대상이 없다.</summary>
        public TargetRef WatchTarget { get; set; }

        /// <summary>OnCompare일 때만 사용.</summary>
        public CompareOperator Operator { get; set; } = CompareOperator.Equals;

        /// <summary>OnCompare일 때만 사용 - 감시 값이 숫자/불리언이면 이 값과 비교한다.</summary>
        public double CompareValue { get; set; }

        /// <summary>OnCompare일 때만 사용 - 감시 값이 문자열이면 이 값과 비교한다(순서 비교는 사전식).</summary>
        public string CompareText { get; set; } = string.Empty;

        /// <summary>OnCompare일 때만 사용 - null이 아니면 상수 대신 이 번지의 현재 값과 비교한다.
        /// 이 번지는 읽기만 하고, 이 번지의 변경으로는 규칙을 실행하지 않는다.</summary>
        public TargetRef CompareTarget { get; set; }

        /// <summary>AND로 붙는 추가 조건. 전부 참이어야 행동을 실행한다. OnTimer에서는 주기가 될 때 거르는
        /// 조건으로만 쓴다. 이 필드가 생기기 전에 저장된 규칙은 빈 목록으로 읽힌다.</summary>
        public List<ScenarioCondition> Conditions { get; set; } = new List<ScenarioCondition>();

        /// <summary>OnTimer일 때만 사용 - 몇 초마다 실행할지.</summary>
        public double TimerIntervalSeconds { get; set; } = 5;

        /// <summary>OnTimer 내부 진행 시간(초). 엔진이 직접 관리하며 저장/복원 대상은 아니다.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public double ElapsedSeconds { get; set; }

        /// <summary>행동으로 값을 쓸 대상.</summary>
        public TargetRef ActionTarget { get; set; }

        /// <summary>쓸 값 - 대상이 숫자/불리언 노드일 때. 숫자 리터럴("5", "-1033") 또는 트리거 값을 가리키는
        /// 'value'를 포함한 간단한 수식("value * 2", "value + 1")을 그대로 문자열로 받는다.</summary>
        public string ActionValueExpression { get; set; } = "0";

        /// <summary>쓸 값 - 대상이 문자열 노드일 때. 수식 계산 없이 그대로 쓴다.</summary>
        public string ActionTextValue { get; set; } = string.Empty;

        public string Describe(Func<Guid, string> moduleNameLookup)
        {
            string moduleName(Guid id) => moduleNameLookup(id) ?? "(missing module)";

            string conditionsText = string.Join(string.Empty,
                (Conditions ?? new List<ScenarioCondition>()).Select((c, i) =>
                    (Trigger == TriggerKind.OnTimer && i == 0 ? " IF " : " AND ") + c.Describe(moduleNameLookup)));

            string triggerText;
            switch (Trigger)
            {
                case TriggerKind.OnChange:
                    triggerText = WatchTarget == null
                        ? "WHEN (no target) changes"
                        : $"WHEN {moduleName(WatchTarget.ModuleId)}.{WatchTarget.DescribeAddress()} changes";
                    break;
                case TriggerKind.OnCompare:
                    triggerText = "WHEN " + DescribeComparison(moduleNameLookup, WatchTarget, Operator, CompareValue, CompareText, CompareTarget);
                    break;
                default:
                    triggerText = $"EVERY {TimerIntervalSeconds}s";
                    break;
            }

            string actionValueText = string.IsNullOrEmpty(ActionTextValue) ? ActionValueExpression : "\"" + ActionTextValue + "\"";
            string actionText = ActionTarget == null
                ? "(no action target)"
                : $"WRITE {actionValueText} TO {moduleName(ActionTarget.ModuleId)}.{ActionTarget.DescribeAddress()}";

            return triggerText + conditionsText + "  →  " + actionText;
        }

        internal static string DescribeComparison(Func<Guid, string> moduleNameLookup, TargetRef target, CompareOperator op,
            double compareValue, string compareText, TargetRef compareTarget)
        {
            string describe(TargetRef t) => (moduleNameLookup(t.ModuleId) ?? "(missing module)") + "." + t.DescribeAddress();

            string rightText = compareTarget != null
                ? describe(compareTarget)
                : string.IsNullOrEmpty(compareText) ? compareValue.ToString() : "\"" + compareText + "\"";

            return (target == null ? "(no target)" : describe(target)) + " " + OperatorSymbol(op) + " " + rightText;
        }

        private static string OperatorSymbol(CompareOperator op)
        {
            switch (op)
            {
                case CompareOperator.Equals: return "==";
                case CompareOperator.NotEquals: return "!=";
                case CompareOperator.GreaterThan: return ">";
                case CompareOperator.GreaterOrEqual: return ">=";
                case CompareOperator.LessThan: return "<";
                case CompareOperator.LessOrEqual: return "<=";
                default: return "?";
            }
        }
    }
}
