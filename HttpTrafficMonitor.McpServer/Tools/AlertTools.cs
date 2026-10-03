using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;

namespace HttpTrafficMonitor.McpServer.Tools;

[McpServerToolType]
public static class AlertTools
{
    [McpServerTool, Description("List all alert rules with their type, status, and configuration.")]
    public static async Task<string> get_alert_rules(IpcClient client)
    {
        try
        {
            var result = await client.GetAsync("/alerts/rules");
            var sb = new StringBuilder();
            sb.AppendLine("=== Alert Rules ===");
            sb.AppendLine();

            if (result.ValueKind == JsonValueKind.Array)
            {
                int count = 0;
                foreach (var rule in result.EnumerateArray())
                {
                    count++;
                    sb.AppendLine($"Rule #{count}:");
                    AppendRule(sb, rule);
                    sb.AppendLine();
                }

                if (count == 0)
                    sb.AppendLine("No alert rules configured.");
            }
            else
            {
                sb.AppendLine("No alert rules configured.");
            }

            return sb.ToString().TrimEnd();
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Add a new alert rule. Supported types: StatusCode, ResponseTime, Domain, Process, RequestSize, ResponseSize.")]
    public static async Task<string> add_alert_rule(
        IpcClient client,
        [Description("Name of the alert rule (required; alert events name the rule that fired)")] string name,
        [Description("Type of alert: StatusCode, ResponseTime, Domain, Process, RequestSize, or ResponseSize")] string type,
        [Description("Text the host name or process name must contain (required for Domain/Process types). Matched as plain text, not as a wildcard: use 'httpbin.org', not '*.httpbin.org' or 'https://httpbin.org', and 'curl', not 'curl.exe'")] string? pattern = null,
        [Description("Minimum status code to trigger alert (for StatusCode type, default 400)")] int? statusCodeMin = null,
        [Description("Maximum status code to trigger alert (for StatusCode type, default 599)")] int? statusCodeMax = null,
        [Description("Response time threshold in milliseconds (required for ResponseTime type)")] int? responseTimeThresholdMs = null,
        [Description("Size threshold in bytes (required for RequestSize/ResponseSize types)")] long? sizeThresholdBytes = null,
        [Description("Whether to play a sound when the alert triggers")] bool playSound = false,
        [Description("Whether the rule is enabled")] bool isEnabled = true)
    {
        try
        {
            var body = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["type"] = type,
                ["playSound"] = playSound,
                ["isEnabled"] = isEnabled
            };

            if (pattern != null) body["pattern"] = pattern;
            if (statusCodeMin.HasValue) body["statusCodeMin"] = statusCodeMin.Value;
            if (statusCodeMax.HasValue) body["statusCodeMax"] = statusCodeMax.Value;
            if (responseTimeThresholdMs.HasValue) body["responseTimeThresholdMs"] = responseTimeThresholdMs.Value;
            if (sizeThresholdBytes.HasValue) body["sizeThresholdBytes"] = sizeThresholdBytes.Value;

            var result = await client.PostAsync("/alerts/rules", body);

            var sb = new StringBuilder();
            sb.AppendLine("Alert rule created successfully.");
            sb.AppendLine();
            AppendRule(sb, result);

            return sb.ToString().TrimEnd();
        }
        catch (IpcApiException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
        {
            // The app names what is wrong: an unknown type, a missing name, or a rule that could never fire
            return $"No alert rule was created: {ex.ApiError ?? ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Delete an alert rule by its ID.")]
    public static async Task<string> delete_alert_rule(
        IpcClient client,
        [Description("The ID of the alert rule to delete")] string ruleId)
    {
        try
        {
            await client.DeleteAsync($"/alerts/rules/{Uri.EscapeDataString(ruleId)}");
            return $"Alert rule '{ruleId}' deleted successfully.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return $"No alert rule with ID '{ruleId}' exists. Use get_alert_rules to list the rules.";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Get recent alert events with timestamps, rule names, and messages.")]
    public static async Task<string> get_alert_events(
        IpcClient client,
        [Description("Number of events to skip (for pagination)")] int skip = 0,
        [Description("Number of events to return")] int take = 20)
    {
        try
        {
            var result = await client.GetAsync($"/alerts/events?skip={skip}&take={take}");
            var sb = new StringBuilder();
            sb.AppendLine("=== Alert Events ===");
            sb.AppendLine();

            // The app sends {events, totalCount}, newest event first
            if (result.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
            {
                int count = 0;
                foreach (var evt in events.EnumerateArray())
                {
                    count++;
                    var timestamp = GetString(evt, "timestamp");
                    var ruleName = GetString(evt, "ruleName");
                    var message = GetString(evt, "message");
                    sb.AppendLine($"[{timestamp}] {ruleName}: {message}");
                }

                if (count == 0)
                    sb.AppendLine("No alert events found.");
                else
                    sb.AppendLine($"\nShowing {count} of {GetString(result, "totalCount")} event(s) (skip={skip}, take={take}).");
            }
            else
            {
                sb.AppendLine("No alert events found.");
            }

            return sb.ToString().TrimEnd();
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Enable or disable alert sounds globally.")]
    public static async Task<string> toggle_alert_sound(
        IpcClient client,
        [Description("True to enable alert sounds, false to disable")] bool enabled)
    {
        try
        {
            await client.PostAsync("/alerts/sound", new { enabled });
            return $"Alert sounds {(enabled ? "enabled" : "disabled")} successfully.";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    // The app answers a rule in the same shape in the rule list and after adding it
    private static void AppendRule(StringBuilder sb, JsonElement rule)
    {
        sb.AppendLine($"  ID:      {GetString(rule, "id")}");
        sb.AppendLine($"  Name:    {GetString(rule, "name")}");
        sb.AppendLine($"  Type:    {GetString(rule, "type")}");
        sb.AppendLine($"  Enabled: {GetString(rule, "isEnabled")}");

        var pattern = GetString(rule, "pattern");
        if (!string.IsNullOrEmpty(pattern))
            sb.AppendLine($"  Pattern: {pattern}");

        if (rule.TryGetProperty("statusCodeMin", out var scMin) && scMin.ValueKind != JsonValueKind.Null)
            sb.AppendLine($"  Status Code Min: {scMin}");
        if (rule.TryGetProperty("statusCodeMax", out var scMax) && scMax.ValueKind != JsonValueKind.Null)
            sb.AppendLine($"  Status Code Max: {scMax}");
        if (rule.TryGetProperty("responseTimeThresholdMs", out var rt) && rt.ValueKind != JsonValueKind.Null)
            sb.AppendLine($"  Response Time Threshold: {rt}ms");
        if (rule.TryGetProperty("sizeThresholdBytes", out var st) && st.ValueKind != JsonValueKind.Null)
            sb.AppendLine($"  Size Threshold: {st} bytes");
        if (rule.TryGetProperty("playSound", out var ps))
            sb.AppendLine($"  Play Sound: {ps}");
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString() ?? "",
                JsonValueKind.Null => "",
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => prop.ToString()
            };
        }
        return "";
    }
}
