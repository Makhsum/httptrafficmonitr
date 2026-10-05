using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;

namespace HttpTrafficMonitor.McpServer.Tools;

[McpServerToolType]
public static class FilterTools
{
    // Offered only while the user allows agent changes in the app (see AgentChangesWatcher);
    // get_exclusions reads the exclusions without it
    internal static readonly string[] ChangeToolNames =
    {
        nameof(manage_exclusions)
    };

    [McpServerTool, Description(
        "Filter captured HTTP requests using advanced conditions. " +
        "Fields: URL, Host, Method, StatusCode, Process, RequestHeaders, RequestBody, ResponseHeaders, ResponseBody. " +
        "Operators: Contains, NotContains, Equals, StartsWith, EndsWith, Regex, GreaterThan, LessThan. " +
        "Example conditions: [{\"field\":\"URL\",\"operator\":\"Contains\",\"value\":\"api\"},{\"field\":\"Method\",\"operator\":\"Equals\",\"value\":\"POST\"}]")]
    public static async Task<string> filter_requests(
        IpcClient client,
        [Description("JSON array of filter conditions. Each condition has field, operator, and value. Example: [{\"field\":\"URL\",\"operator\":\"Contains\",\"value\":\"api\"}]")] string conditions,
        [Description("Logic operator to combine conditions: AND or OR. Default: AND")] string logicOperator = "AND",
        [Description("Number of results to skip for pagination. Default: 0")] int skip = 0,
        [Description("Number of results to return. Default: 20")] int take = 20)
    {
        try
        {
            JsonElement parsedConditions;
            try
            {
                parsedConditions = JsonSerializer.Deserialize<JsonElement>(conditions);
            }
            catch (JsonException)
            {
                return "Error: 'conditions' must be a valid JSON array. Example: [{\"field\":\"URL\",\"operator\":\"Contains\",\"value\":\"api\"}]";
            }

            var body = new
            {
                conditions = parsedConditions,
                logicOperator,
                skip,
                take
            };

            var result = await client.PostAsync("/requests/filter", body);
            return TrafficTools.FormatRequestList(result);
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Get the domain and process exclusions. Excluded domains and processes will not be captured by the proxy.")]
    public static async Task<string> get_exclusions(IpcClient client)
    {
        try
        {
            var result = await client.GetAsync("/exclusions");
            return FormatExclusions(result);
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Get or set domain and process exclusions. Excluded domains and processes will not be captured by the proxy.")]
    public static async Task<string> manage_exclusions(
        IpcClient client,
        [Description("Action to perform: 'get' to retrieve current exclusions, 'set' to update them")] string action,
        [Description("Comma-separated list of domains to exclude (only for 'set' action). Example: \"example.com,ads.tracker.net\"")] string? domains = null,
        [Description("Comma-separated list of process names to exclude (only for 'set' action). Example: \"chrome,firefox\"")] string? processes = null)
    {
        try
        {
            if (action.Equals("get", StringComparison.OrdinalIgnoreCase))
            {
                var result = await client.GetAsync("/exclusions");
                return FormatExclusions(result);
            }
            else if (action.Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                var domainList = string.IsNullOrEmpty(domains)
                    ? Array.Empty<string>()
                    : domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var processList = string.IsNullOrEmpty(processes)
                    ? Array.Empty<string>()
                    : processes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var body = new
                {
                    domains = domainList,
                    processes = processList
                };

                await client.PostAsync("/exclusions", body);

                var sb = new StringBuilder();
                sb.AppendLine("Exclusions updated successfully.");
                sb.AppendLine();
                sb.AppendLine($"Excluded domains ({domainList.Length}):");
                foreach (var d in domainList) sb.AppendLine($"  - {d}");
                if (domainList.Length == 0) sb.AppendLine("  (none)");
                sb.AppendLine();
                sb.AppendLine($"Excluded processes ({processList.Length}):");
                foreach (var p in processList) sb.AppendLine($"  - {p}");
                if (processList.Length == 0) sb.AppendLine("  (none)");

                return sb.ToString().TrimEnd();
            }
            else
            {
                return "Error: action must be 'get' or 'set'.";
            }
        }
        catch (IpcApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            // The app refuses changes from agents until the user allows them
            return $"The exclusions were not changed: {ex.ApiError ?? ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    private static string FormatExclusions(JsonElement result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Exclusions ===");
        sb.AppendLine();

        sb.AppendLine("Excluded Domains:");
        if (result.TryGetProperty("domains", out var domainsEl) && domainsEl.ValueKind == JsonValueKind.Array)
        {
            if (domainsEl.GetArrayLength() == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var d in domainsEl.EnumerateArray())
                    sb.AppendLine($"  - {d.GetString()}");
            }
        }
        else
        {
            sb.AppendLine("  (none)");
        }

        sb.AppendLine();
        sb.AppendLine("Excluded Processes:");
        if (result.TryGetProperty("processes", out var processesEl) && processesEl.ValueKind == JsonValueKind.Array)
        {
            if (processesEl.GetArrayLength() == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var p in processesEl.EnumerateArray())
                    sb.AppendLine($"  - {p.GetString()}");
            }
        }
        else
        {
            sb.AppendLine("  (none)");
        }

        return sb.ToString().TrimEnd();
    }

    [McpServerTool, Description("List all saved filter presets that can be used to quickly apply common filter configurations.")]
    public static async Task<string> get_filter_presets(IpcClient client)
    {
        try
        {
            var result = await client.GetAsync("/filter-presets");
            var sb = new StringBuilder();
            sb.AppendLine("=== Filter Presets ===");
            sb.AppendLine();

            JsonElement presets;
            if (result.ValueKind == JsonValueKind.Array)
            {
                presets = result;
            }
            else if (result.TryGetProperty("presets", out var presetsEl))
            {
                presets = presetsEl;
            }
            else
            {
                return "No filter presets found.";
            }

            if (presets.GetArrayLength() == 0)
            {
                sb.AppendLine("No filter presets saved yet.");
                return sb.ToString().TrimEnd();
            }

            foreach (var preset in presets.EnumerateArray())
            {
                var name = preset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : "?";
                var logic = preset.TryGetProperty("logicOperator", out var logicEl) ? logicEl.GetString() : "AND";

                sb.AppendLine($"Preset: {name} (Logic: {logic})");

                if (preset.TryGetProperty("conditions", out var conditions) && conditions.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cond in conditions.EnumerateArray())
                    {
                        var field = cond.TryGetProperty("field", out var fEl) ? fEl.GetString() : "?";
                        var op = cond.TryGetProperty("operator", out var oEl) ? oEl.GetString() : "?";
                        var val = cond.TryGetProperty("value", out var vEl) ? vEl.GetString() : "?";
                        sb.AppendLine($"  {field} {op} \"{val}\"");
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description(
        "Save a new filter preset for reuse. " +
        "Example conditions: [{\"field\":\"Method\",\"operator\":\"Equals\",\"value\":\"POST\"},{\"field\":\"URL\",\"operator\":\"Contains\",\"value\":\"/api\"}]")]
    public static async Task<string> save_filter_preset(
        IpcClient client,
        [Description("Name for the filter preset")] string name,
        [Description("JSON array of filter conditions")] string conditions,
        [Description("Logic operator: AND or OR. Default: AND")] string logicOperator = "AND")
    {
        try
        {
            JsonElement parsedConditions;
            try
            {
                parsedConditions = JsonSerializer.Deserialize<JsonElement>(conditions);
            }
            catch (JsonException)
            {
                return "Error: 'conditions' must be a valid JSON array. Example: [{\"field\":\"URL\",\"operator\":\"Contains\",\"value\":\"api\"}]";
            }

            var body = new
            {
                name,
                logicOperator,
                conditions = parsedConditions
            };

            await client.PostAsync("/filter-presets", body);
            return $"Filter preset '{name}' saved successfully.";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Delete a saved filter preset by name.")]
    public static async Task<string> delete_filter_preset(
        IpcClient client,
        [Description("Name of the filter preset to delete")] string name)
    {
        try
        {
            await client.DeleteAsync($"/filter-presets/{Uri.EscapeDataString(name)}");
            return $"Filter preset '{name}' deleted successfully.";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }
}
