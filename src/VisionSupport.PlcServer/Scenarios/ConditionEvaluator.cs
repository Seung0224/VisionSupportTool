using System;
using System.Collections.Generic;
using VirtualPlcServer.Modules;

namespace VirtualPlcServer.Scenarios
{
    /// <summary>
    /// 규칙의 비교(주 조건과 AND 추가 조건)를 평가한다. 엔진은 언제 평가할지만 정하고, 무엇이 참인지는
    /// 여기서 정한다. 번지를 읽을 수 없으면(모듈 삭제, 범위 밖 번지, 없는 노드) 그 비교는 거짓이다 -
    /// 시나리오 하나가 잘못 설정됐다고 맵 이벤트를 올린 통신 스레드까지 예외가 번지면 안 된다.
    /// </summary>
    public static class ConditionEvaluator
    {
        public static bool IsMet(ScenarioCondition condition, Func<Guid, PlcServerModule> moduleLookup)
        {
            if (condition?.Target == null)
            {
                return false;
            }

            object left = Read(condition.Target, moduleLookup);
            return left != null && Compare(left, condition.Operator, condition.CompareValue, condition.CompareText,
                condition.CompareTarget, moduleLookup);
        }

        public static bool AllMet(IEnumerable<ScenarioCondition> conditions, Func<Guid, PlcServerModule> moduleLookup)
        {
            if (conditions == null)
            {
                return true;
            }

            foreach (ScenarioCondition condition in conditions)
            {
                if (!IsMet(condition, moduleLookup))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>왼쪽 값을 상수(compareTarget이 null일 때) 또는 compareTarget 번지의 현재 값과 비교한다.
        /// 양쪽이 모두 문자열이면 문자열로, 아니면 숫자로 비교한다.</summary>
        public static bool Compare(object left, CompareOperator op, double compareValue, string compareText,
            TargetRef compareTarget, Func<Guid, PlcServerModule> moduleLookup)
        {
            if (compareTarget != null)
            {
                object right = Read(compareTarget, moduleLookup);
                if (right == null)
                {
                    return false;
                }

                return left is string leftText && right is string rightText
                    ? CompareText(leftText, op, rightText)
                    : CompareNumeric(PlcTargetAccessor.ToDouble(left), op, PlcTargetAccessor.ToDouble(right));
            }

            return left is string text
                ? CompareText(text, op, compareText ?? string.Empty)
                : CompareNumeric(PlcTargetAccessor.ToDouble(left), op, compareValue);
        }

        public static object Read(TargetRef target, Func<Guid, PlcServerModule> moduleLookup)
        {
            PlcServerModule module = moduleLookup(target.ModuleId);
            if (module == null)
            {
                return null;
            }

            try
            {
                return PlcTargetAccessor.ReadRaw(module, target);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (OverflowException)
            {
                return null;
            }
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
