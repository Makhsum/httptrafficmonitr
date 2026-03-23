using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HttpTrafficMonitor.Models;

namespace HttpTrafficMonitor.Services
{
    public class AlertService
    {
        public ObservableCollection<AlertRule> Rules { get; } = new();
        public event Action<AlertEvent>? AlertTriggered;

        public bool SoundEnabled { get; set; } = true;

        public void Evaluate(HttpRequestEntry entry)
        {
            foreach (var rule in Rules.Where(r => r.IsEnabled))
            {
                string? message = EvaluateRule(rule, entry);
                if (message != null)
                {
                    var alertEvent = new AlertEvent
                    {
                        Rule = rule,
                        Request = entry,
                        Message = message
                    };
                    AlertTriggered?.Invoke(alertEvent);

                    if (rule.PlaySound && SoundEnabled)
                    {
                        try { System.Media.SystemSounds.Exclamation.Play(); }
                        catch { }
                    }
                }
            }
        }

        private static string? EvaluateRule(AlertRule rule, HttpRequestEntry entry)
        {
            switch (rule.Type)
            {
                case AlertRuleType.StatusCode:
                    if (entry.StatusCode.HasValue)
                    {
                        int code = entry.StatusCode.Value;
                        int min = rule.StatusCodeMin ?? 400;
                        int max = rule.StatusCodeMax ?? 599;
                        if (code >= min && code <= max)
                            return $"Status {code} on {entry.Url}";
                    }
                    break;

                case AlertRuleType.ResponseTime:
                    if (entry.Duration.HasValue && rule.ResponseTimeThresholdMs.HasValue)
                    {
                        if (entry.Duration.Value.TotalMilliseconds > rule.ResponseTimeThresholdMs.Value)
                            return $"Slow response ({entry.DurationFormatted}) on {entry.Url}";
                    }
                    break;

                case AlertRuleType.Domain:
                    if (!string.IsNullOrEmpty(rule.Pattern) &&
                        entry.Host.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
                        return $"Request to watched domain {entry.Host}: {entry.Url}";
                    break;

                case AlertRuleType.Process:
                    if (!string.IsNullOrEmpty(rule.Pattern) &&
                        entry.ProcessName.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
                        return $"Request from watched process {entry.ProcessName}: {entry.Url}";
                    break;

                case AlertRuleType.RequestSize:
                    if (rule.SizeThresholdBytes.HasValue &&
                        entry.RequestBody.Length > rule.SizeThresholdBytes.Value)
                        return $"Large request body ({entry.RequestBody.Length} bytes) on {entry.Url}";
                    break;

                case AlertRuleType.ResponseSize:
                    if (rule.SizeThresholdBytes.HasValue &&
                        entry.ResponseSize.HasValue && entry.ResponseSize.Value > rule.SizeThresholdBytes.Value)
                        return $"Large response ({entry.ResponseSizeFormatted}) on {entry.Url}";
                    break;
            }
            return null;
        }

        public void AddDefaultRules()
        {
            Rules.Add(new AlertRule
            {
                Name = "Server Errors (5xx)",
                Type = AlertRuleType.StatusCode,
                StatusCodeMin = 500,
                StatusCodeMax = 599,
                PlaySound = true
            });
            Rules.Add(new AlertRule
            {
                Name = "Slow Responses (>5s)",
                Type = AlertRuleType.ResponseTime,
                ResponseTimeThresholdMs = 5000,
                IsEnabled = false
            });
        }
    }
}
