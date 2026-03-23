using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HttpTrafficMonitor.Models
{
    public class FilterPreset
    {
        public string Name { get; set; } = "New Filter";
        public string LogicOperator { get; set; } = "AND";
        public List<FilterCondition> Conditions { get; set; } = new();

        public bool Evaluate(HttpRequestEntry entry)
        {
            if (Conditions.Count == 0) return true;

            if (LogicOperator == "OR")
                return Conditions.Exists(c => c.Evaluate(entry));

            return Conditions.TrueForAll(c => c.Evaluate(entry));
        }
    }

    public class FilterCondition
    {
        public string Field { get; set; } = "URL";
        public string Operator { get; set; } = "Contains";
        public string Value { get; set; } = string.Empty;

        public static readonly string[] Fields = { "URL", "Host", "Method", "StatusCode", "Process", "RequestHeaders", "RequestBody", "ResponseHeaders", "ResponseBody" };
        public static readonly string[] Operators = { "Contains", "NotContains", "Equals", "StartsWith", "EndsWith", "Regex", "GreaterThan", "LessThan" };

        public bool Evaluate(HttpRequestEntry entry)
        {
            string fieldValue = Field switch
            {
                "URL" => entry.Url,
                "Host" => entry.Host,
                "Method" => entry.Method,
                "StatusCode" => entry.StatusCode?.ToString() ?? "",
                "Process" => entry.ProcessName,
                "RequestHeaders" => entry.RequestHeaders,
                "RequestBody" => entry.RequestBody,
                "ResponseHeaders" => entry.ResponseHeaders ?? "",
                "ResponseBody" => entry.ResponseBody ?? "",
                _ => ""
            };

            return Operator switch
            {
                "Contains" => fieldValue.Contains(Value, StringComparison.OrdinalIgnoreCase),
                "NotContains" => !fieldValue.Contains(Value, StringComparison.OrdinalIgnoreCase),
                "Equals" => fieldValue.Equals(Value, StringComparison.OrdinalIgnoreCase),
                "StartsWith" => fieldValue.StartsWith(Value, StringComparison.OrdinalIgnoreCase),
                "EndsWith" => fieldValue.EndsWith(Value, StringComparison.OrdinalIgnoreCase),
                "Regex" => TryRegex(fieldValue, Value),
                "GreaterThan" => double.TryParse(fieldValue, out var a) && double.TryParse(Value, out var b) && a > b,
                "LessThan" => double.TryParse(fieldValue, out var c) && double.TryParse(Value, out var d) && c < d,
                _ => false
            };
        }

        private static bool TryRegex(string input, string pattern)
        {
            try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase); }
            catch { return false; }
        }
    }
}
