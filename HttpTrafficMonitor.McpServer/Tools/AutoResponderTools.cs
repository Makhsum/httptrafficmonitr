using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace HttpTrafficMonitor.McpServer.Tools;

[McpServerToolType]
public static class AutoResponderTools
{
    [McpServerTool, Description("Get the current status of the auto-responder, including whether it is enabled and how many rules are configured.")]
    public static async Task<string> get_auto_responder_status(IpcClient client)
    {
        try
        {
            var result = await client.GetAsync("/auto-responder/status");
            var enabled = GetString(result, "enabled");
            if (string.IsNullOrEmpty(enabled))
                enabled = GetString(result, "isEnabled");

            var ruleCount = GetString(result, "ruleCount");

            var sb = new StringBuilder();
            sb.AppendLine("=== Auto-Responder Status ===");
            sb.AppendLine();
            sb.AppendLine($"  Enabled:    {enabled}");
            sb.AppendLine($"  Rule Count: {ruleCount}");

            return sb.ToString().TrimEnd();
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Enable or disable the auto-responder.")]
    public static async Task<string> toggle_auto_responder(
        IpcClient client,
        [Description("True to enable the auto-responder, false to disable")] bool enabled)
    {
        try
        {
            await client.PostAsync("/auto-responder/toggle", new { enabled });
            return $"Auto-responder {(enabled ? "enabled" : "disabled")} successfully.";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("List all auto-responder rules with their URL pattern, HTTP method, response status code, and match count.")]
    public static async Task<string> get_auto_responder_rules(IpcClient client)
    {
        try
        {
            var result = await client.GetAsync("/auto-responder/rules");
            var sb = new StringBuilder();
            sb.AppendLine("=== Auto-Responder Rules ===");
            sb.AppendLine();

            if (result.ValueKind == JsonValueKind.Array)
            {
                int count = 0;
                foreach (var rule in result.EnumerateArray())
                {
                    count++;
                    sb.AppendLine($"Rule #{count}:");
                    sb.AppendLine($"  ID:          {GetString(rule, "id")}");
                    sb.AppendLine($"  URL Pattern: {GetString(rule, "urlPattern")}");
                    sb.AppendLine($"  Is Regex:    {GetString(rule, "isRegex")}");
                    sb.AppendLine($"  Method:      {GetString(rule, "httpMethod")}");
                    sb.AppendLine($"  Status Code: {GetString(rule, "responseStatusCode")}");
                    sb.AppendLine($"  Match Count: {GetString(rule, "matchCount")}");
                    sb.AppendLine($"  Enabled:     {GetString(rule, "isEnabled")}");

                    var delay = GetString(rule, "delayMs");
                    if (!string.IsNullOrEmpty(delay) && delay != "0")
                        sb.AppendLine($"  Delay:       {delay}ms");

                    sb.AppendLine();
                }

                if (count == 0)
                    sb.AppendLine("No auto-responder rules configured.");
            }
            else
            {
                sb.AppendLine("No auto-responder rules configured.");
            }

            return sb.ToString().TrimEnd();
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Add a new auto-responder rule to intercept matching requests and return a custom response.")]
    public static async Task<string> add_auto_responder_rule(
        IpcClient client,
        [Description("URL pattern to match against incoming requests")] string urlPattern,
        [Description("Whether the URL pattern is a regular expression")] bool isRegex = false,
        [Description("HTTP method to match (e.g., GET, POST, ANY)")] string httpMethod = "ANY",
        [Description("HTTP status code to return in the response")] int responseStatusCode = 200,
        [Description("Response headers (e.g., 'Content-Type: application/json')")] string responseHeaders = "Content-Type: application/json",
        [Description("Response body content")] string responseBody = "",
        [Description("Delay in milliseconds before sending the response")] int delayMs = 0,
        [Description("Whether the rule is enabled")] bool isEnabled = true)
    {
        try
        {
            var body = new
            {
                urlPattern,
                isRegex,
                httpMethod,
                responseStatusCode,
                responseHeaders,
                responseBody,
                delayMs,
                isEnabled
            };

            var result = await client.PostAsync("/auto-responder/rules", body);

            var sb = new StringBuilder();
            sb.AppendLine("Auto-responder rule created successfully.");
            sb.AppendLine();
            sb.AppendLine($"  ID:          {GetString(result, "id")}");
            sb.AppendLine($"  URL Pattern: {GetString(result, "urlPattern")}");
            sb.AppendLine($"  Method:      {GetString(result, "httpMethod")}");
            sb.AppendLine($"  Status Code: {GetString(result, "responseStatusCode")}");
            sb.AppendLine($"  Enabled:     {GetString(result, "isEnabled")}");

            return sb.ToString().TrimEnd();
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Update an existing auto-responder rule. Only provided fields will be updated.")]
    public static async Task<string> update_auto_responder_rule(
        IpcClient client,
        [Description("The ID of the rule to update")] string ruleId,
        [Description("New URL pattern to match")] string? urlPattern = null,
        [Description("Whether the URL pattern is a regular expression")] bool? isRegex = null,
        [Description("HTTP method to match (e.g., GET, POST, ANY)")] string? httpMethod = null,
        [Description("HTTP status code to return")] int? responseStatusCode = null,
        [Description("Response headers")] string? responseHeaders = null,
        [Description("Response body content")] string? responseBody = null,
        [Description("Delay in milliseconds before sending the response")] int? delayMs = null,
        [Description("Whether the rule is enabled")] bool? isEnabled = null)
    {
        try
        {
            var body = new Dictionary<string, object>();

            if (urlPattern != null) body["urlPattern"] = urlPattern;
            if (isRegex.HasValue) body["isRegex"] = isRegex.Value;
            if (httpMethod != null) body["httpMethod"] = httpMethod;
            if (responseStatusCode.HasValue) body["responseStatusCode"] = responseStatusCode.Value;
            if (responseHeaders != null) body["responseHeaders"] = responseHeaders;
            if (responseBody != null) body["responseBody"] = responseBody;
            if (delayMs.HasValue) body["delayMs"] = delayMs.Value;
            if (isEnabled.HasValue) body["isEnabled"] = isEnabled.Value;

            if (body.Count == 0)
                return "No fields provided to update. Please specify at least one field to change.";

            var result = await client.PutAsync($"/auto-responder/rules/{ruleId}", body);

            var sb = new StringBuilder();
            sb.AppendLine($"Auto-responder rule '{ruleId}' updated successfully.");
            sb.AppendLine();
            sb.AppendLine($"  ID:          {GetString(result, "id")}");
            sb.AppendLine($"  URL Pattern: {GetString(result, "urlPattern")}");
            sb.AppendLine($"  Method:      {GetString(result, "httpMethod")}");
            sb.AppendLine($"  Status Code: {GetString(result, "responseStatusCode")}");
            sb.AppendLine($"  Enabled:     {GetString(result, "isEnabled")}");

            return sb.ToString().TrimEnd();
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Delete an auto-responder rule by its ID.")]
    public static async Task<string> delete_auto_responder_rule(
        IpcClient client,
        [Description("The ID of the auto-responder rule to delete")] string ruleId)
    {
        try
        {
            await client.DeleteAsync($"/auto-responder/rules/{ruleId}");
            return $"Auto-responder rule '{ruleId}' deleted successfully.";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
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
