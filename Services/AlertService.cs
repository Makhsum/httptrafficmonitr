using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HttpTrafficMonitor.Models;

namespace HttpTrafficMonitor.Services
{
    public class AlertService
    {
        // A StatusCode rule without a range watches the error codes
        public const int DefaultStatusCodeMin = 400;
        public const int DefaultStatusCodeMax = 599;

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
                        int min = rule.StatusCodeMin ?? DefaultStatusCodeMin;
                        int max = rule.StatusCodeMax ?? DefaultStatusCodeMax;
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

        // Says why EvaluateRule could never return a message for the rule, or null when it can fire
        public static string? FindWhyRuleCannotFire(AlertRule rule)
        {
            switch (rule.Type)
            {
                case AlertRuleType.StatusCode:
                    int min = rule.StatusCodeMin ?? DefaultStatusCodeMin;
                    int max = rule.StatusCodeMax ?? DefaultStatusCodeMax;
                    if (min > max)
                        return $"the status code range {min}-{max} is empty: the minimum {min} is above the maximum {max}" +
                               (rule.StatusCodeMin.HasValue && rule.StatusCodeMax.HasValue ? "" : $" (without statusCodeMin/statusCodeMax a StatusCode rule covers {DefaultStatusCodeMin}-{DefaultStatusCodeMax})") +
                               ", so no status code can match.";
                    if (max < 100 || min > 999)
                        return $"the status code range {min}-{max} holds no HTTP status code: status codes have three digits, from 100 to 999.";
                    break;

                case AlertRuleType.ResponseTime:
                    if (!rule.ResponseTimeThresholdMs.HasValue)
                        return "a ResponseTime rule needs responseTimeThresholdMs, the response time in milliseconds above which it fires.";
                    break;

                case AlertRuleType.Domain:
                case AlertRuleType.Process:
                    string matched = rule.Type == AlertRuleType.Domain ? "host name" : "process name";
                    if (string.IsNullOrWhiteSpace(rule.Pattern))
                        return $"a {rule.Type} rule needs a pattern, the text the {matched} must contain.";
                    if (rule.Pattern.IndexOfAny(new[] { '*', '?' }) >= 0)
                    {
                        string plain = rule.Pattern.Trim('*', '?', '.');
                        return $"the pattern '{rule.Pattern}' is matched as plain text anywhere in the {matched}, not as a wildcard, " +
                               $"and no {matched} contains '*' or '?'. " +
                               (plain.Length > 0 && plain.IndexOfAny(new[] { '*', '?' }) < 0
                                   ? $"Use '{plain}' instead: it matches every {matched} that contains it."
                                   : $"Leave the wildcard out: the pattern matches every {matched} that contains it.");
                    }
                    string trimmed = rule.Pattern.Trim();
                    if (rule.Type == AlertRuleType.Domain && trimmed.Contains('/'))
                    {
                        string host = trimmed.Contains("://") ? trimmed[(trimmed.IndexOf("://") + 3)..] : trimmed;
                        host = host.Split('/')[0];
                        return $"the pattern '{rule.Pattern}' is matched against the host name only, and no host name contains '/'. " +
                               (host.Length > 0 ? $"Use '{host}' instead." : "Use the host name alone.");
                    }
                    if (rule.Type == AlertRuleType.Process && trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        return $"the pattern '{rule.Pattern}' is matched against the process name, which never ends in '.exe'. " +
                               (trimmed.Length > 4 ? $"Use '{trimmed[..^4]}' instead." : "Use the process name without '.exe'.");
                    if (rule.Pattern != trimmed)
                        return $"the pattern '{rule.Pattern}' starts or ends with a space, and no {matched} does. Use '{trimmed}' instead.";
                    break;

                case AlertRuleType.RequestSize:
                case AlertRuleType.ResponseSize:
                    if (!rule.SizeThresholdBytes.HasValue)
                        return $"a {rule.Type} rule needs sizeThresholdBytes, the size in bytes above which it fires.";
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
