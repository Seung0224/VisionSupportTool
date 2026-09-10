using System;
using System.Collections.Generic;
using System.Globalization;

namespace VirtualPlcServer.Scenarios
{
    /// <summary>
    /// 액션 값 칸에 적는 아주 작은 수식을 계산한다. 숫자 리터럴("5", "-1033")은 물론,
    /// 트리거에서 읽은 값을 가리키는 'value' 토큰과 +,-,*,/,()를 지원한다("value * 2", "-(value + 1)").
    /// </summary>
    public static class ExpressionEvaluator
    {
        public static double Evaluate(string expression, double triggerValue)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return 0;
            }

            List<string> tokens = Tokenize(expression);
            int position = 0;
            double result = ParseExpression(tokens, ref position, triggerValue);

            if (position != tokens.Count)
            {
                throw new FormatException("Unexpected token '" + tokens[position] + "' in expression '" + expression + "'.");
            }

            return result;
        }

        private static List<string> Tokenize(string expression)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < expression.Length)
            {
                char c = expression[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if ("+-*/()".IndexOf(c) >= 0)
                {
                    tokens.Add(c.ToString());
                    i++;
                    continue;
                }

                if (char.IsDigit(c) || c == '.')
                {
                    int start = i;
                    while (i < expression.Length && (char.IsDigit(expression[i]) || expression[i] == '.'))
                    {
                        i++;
                    }

                    tokens.Add(expression.Substring(start, i - start));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_'))
                    {
                        i++;
                    }

                    tokens.Add(expression.Substring(start, i - start));
                    continue;
                }

                throw new FormatException("Unexpected character '" + c + "' in expression '" + expression + "'.");
            }

            return tokens;
        }

        private static double ParseExpression(List<string> tokens, ref int pos, double triggerValue)
        {
            double left = ParseTerm(tokens, ref pos, triggerValue);
            while (pos < tokens.Count && (tokens[pos] == "+" || tokens[pos] == "-"))
            {
                string op = tokens[pos];
                pos++;
                double right = ParseTerm(tokens, ref pos, triggerValue);
                left = op == "+" ? left + right : left - right;
            }

            return left;
        }

        private static double ParseTerm(List<string> tokens, ref int pos, double triggerValue)
        {
            double left = ParseFactor(tokens, ref pos, triggerValue);
            while (pos < tokens.Count && (tokens[pos] == "*" || tokens[pos] == "/"))
            {
                string op = tokens[pos];
                pos++;
                double right = ParseFactor(tokens, ref pos, triggerValue);
                left = op == "*" ? left * right : left / right;
            }

            return left;
        }

        private static double ParseFactor(List<string> tokens, ref int pos, double triggerValue)
        {
            if (pos >= tokens.Count)
            {
                throw new FormatException("Unexpected end of expression.");
            }

            string token = tokens[pos];

            if (token == "-")
            {
                pos++;
                return -ParseFactor(tokens, ref pos, triggerValue);
            }

            if (token == "+")
            {
                pos++;
                return ParseFactor(tokens, ref pos, triggerValue);
            }

            if (token == "(")
            {
                pos++;
                double inner = ParseExpression(tokens, ref pos, triggerValue);
                if (pos >= tokens.Count || tokens[pos] != ")")
                {
                    throw new FormatException("Missing closing parenthesis.");
                }

                pos++;
                return inner;
            }

            if (string.Equals(token, "value", StringComparison.OrdinalIgnoreCase))
            {
                pos++;
                return triggerValue;
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                pos++;
                return number;
            }

            throw new FormatException("Unexpected token '" + token + "'.");
        }
    }
}
