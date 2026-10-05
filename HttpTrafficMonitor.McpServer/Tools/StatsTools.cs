using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace HttpTrafficMonitor.McpServer.Tools;

[McpServerToolType]
public static class StatsTools
{
    [McpServerTool, Description("Get traffic statistics including total requests, method breakdown, error count, data transferred, slowest requests, and top domains/processes.")]
    public static async Task<string> get_traffic_stats(IpcClient client)
    {
        try
        {
            var result = await client.GetAsync("/stats");
            var sb = new StringBuilder();
            sb.AppendLine("=== Traffic Statistics ===");
            sb.AppendLine();

            if (result.TryGetProperty("totalRequests", out var totalEl))
                sb.AppendLine($"Total Requests:    {totalEl}");
            if (result.TryGetProperty("errorCount", out var errEl))
                sb.AppendLine($"Errors:            {errEl}");
            if (result.TryGetProperty("totalBytesFormatted", out var dataEl))
                sb.AppendLine($"Data Transferred:  {dataEl.GetString()}");
            if (result.TryGetProperty("slowRequestCount", out var slowCountEl))
                sb.AppendLine($"Slow Requests:     {slowCountEl}");

            sb.AppendLine();

            // Method breakdown, the same four counters as the app's status bar
            sb.AppendLine("--- Method Breakdown ---");
            foreach (var (method, key) in new[] { ("GET", "getCount"), ("POST", "postCount"), ("PUT", "putCount"), ("DELETE", "deleteCount") })
            {
                if (result.TryGetProperty(key, out var countEl))
                    sb.AppendLine($"  {method,-8} {countEl}");
            }
            sb.AppendLine();

            // Slow requests
            if (result.TryGetProperty("slowestRequests", out var slowEl) && slowEl.ValueKind == JsonValueKind.Array && slowEl.GetArrayLength() > 0)
            {
                sb.AppendLine("--- Slowest Requests ---");
                foreach (var req in slowEl.EnumerateArray())
                {
                    var id = req.TryGetProperty("id", out var idEl) ? idEl.ToString() : "?";
                    var url = req.TryGetProperty("url", out var uEl) ? uEl.GetString() : "?";
                    var duration = TrafficTools.FormatDuration(req);

                    if (url != null && url.Length > 60)
                        url = url[..57] + "...";

                    sb.AppendLine($"  [{id}] {url} ({duration})");
                }
                sb.AppendLine();
            }

            // Domain distribution (top 10)
            if (result.TryGetProperty("domainDistribution", out var domainsEl))
            {
                sb.AppendLine("--- Top Domains ---");
                FormatDistribution(sb, domainsEl, "domain", 10);
                sb.AppendLine();
            }

            // Process distribution (top 10)
            if (result.TryGetProperty("processDistribution", out var processesEl))
            {
                sb.AppendLine("--- Top Processes ---");
                FormatDistribution(sb, processesEl, "process", 10);
            }

            return TrafficTools.WithCredentialsNotice(sb.ToString().TrimEnd(), result);
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Get a list of all unique domains (hosts) seen in captured traffic, sorted by request count.")]
    public static async Task<string> get_all_domains(IpcClient client)
    {
        try
        {
            var result = await client.GetAsync("/domains");
            var sb = new StringBuilder();
            sb.AppendLine("=== All Captured Domains ===");
            sb.AppendLine();

            JsonElement domains;
            if (result.TryGetProperty("domains", out var domainsEl))
                domains = domainsEl;
            else if (result.ValueKind == JsonValueKind.Array)
                domains = result;
            else
                return "No domains found.";

            if (domains.GetArrayLength() == 0)
                return "No domains captured yet.";

            int i = 0;
            foreach (var item in domains.EnumerateArray())
            {
                i++;
                var name = item.TryGetProperty("name", out var nEl) ? nEl.GetString() : "?";
                var count = item.TryGetProperty("count", out var cEl) ? cEl.GetInt32() : 0;
                sb.AppendLine($"  {i,3}. {name,-45} {count} requests");
            }

            sb.AppendLine();
            sb.AppendLine($"Total unique domains: {i}");
            return sb.ToString().TrimEnd();
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Compare two HTTP requests side by side, showing differences in method, URL, headers, body, status code, and timing.")]
    public static async Task<string> compare_requests(
        IpcClient client,
        [Description("ID of the first request to compare")] int requestId1,
        [Description("ID of the second request to compare")] int requestId2)
    {
        try
        {
            var body = new { requestId1, requestId2 };
            var result = await client.PostAsync("/compare", body);
            var sb = new StringBuilder();

            sb.AppendLine($"=== Comparison: Request #{requestId1} vs #{requestId2} ===");
            sb.AppendLine();

            FormatComparisonProperties(sb, result, requestId1, requestId2);
            FormatTimingComparison(sb, result, requestId1, requestId2);

            // Line-by-line differences, the same four diffs as the tabs of the app's comparison view
            if (result.TryGetProperty("diffs", out var diffsEl) && diffsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var (title, key) in new[] { ("Req Headers", "requestHeaders"), ("Req Body", "requestBody"), ("Resp Headers", "responseHeaders"), ("Resp Body", "responseBody") })
                {
                    sb.AppendLine($"--- {title} ---");
                    var diff = diffsEl.TryGetProperty(key, out var diffEl) ? diffEl.GetString() : null;
                    FormatDiffLines(sb, diff ?? "", 100);
                    sb.AppendLine();
                }
            }

            return TrafficTools.WithCredentialsNotice(sb.ToString().TrimEnd(), result);
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    private static void FormatDistribution(StringBuilder sb, JsonElement element, string keyName, int limit)
    {
        int count = 0;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (count >= limit) break;
                sb.AppendLine($"  {prop.Name,-35} {prop.Value}");
                count++;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (count >= limit) break;
                var name = item.TryGetProperty(keyName, out var nEl)
                    ? nEl.GetString()
                    : item.TryGetProperty("name", out var n2El)
                        ? n2El.GetString()
                        : "?";
                var cnt = item.TryGetProperty("count", out var cEl) ? cEl.ToString() : "?";
                sb.AppendLine($"  {name,-35} {cnt}");
                count++;
            }
        }

        if (count == 0)
            sb.AppendLine("  (no data)");
    }

    private static string FormatDiffValue(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? "(null)",
            JsonValueKind.Null => "(null)",
            JsonValueKind.Undefined => "(undefined)",
            _ => el.ToString()
        };
    }

    // Same line as the app's comparison view shows above its diff; empty while one of them is pending
    private static void FormatTimingComparison(StringBuilder sb, JsonElement result, int id1, int id2)
    {
        if (!result.TryGetProperty("request1", out var r1) || !result.TryGetProperty("request2", out var r2))
            return;
        if (!r1.TryGetProperty("durationMs", out var d1) || d1.ValueKind != JsonValueKind.Number
            || !r2.TryGetProperty("durationMs", out var d2) || d2.ValueKind != JsonValueKind.Number)
            return;

        sb.AppendLine($"Timing: #{id1}: {TrafficTools.FormatDuration(r1)}  |  #{id2}: {TrafficTools.FormatDuration(r2)}  |  \u0394: {Math.Abs(d1.GetDouble() - d2.GetDouble()):F0} ms");
        sb.AppendLine();
    }

    // Keeps the changed lines of a "+ "/"- "/"~ " prefixed diff from the app and leaves out the unchanged ones
    private static void FormatDiffLines(StringBuilder sb, string diff, int maxLines)
    {
        var changed = diff.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("+ ") || line.StartsWith("- ") || line.StartsWith("~ "))
            .ToList();

        if (changed.Count == 0)
        {
            sb.AppendLine("(no differences)");
            return;
        }

        foreach (var line in changed.Take(maxLines))
            sb.AppendLine(TruncateValue(line, 200));
        if (changed.Count > maxLines)
            sb.AppendLine($"... {changed.Count - maxLines} more changed lines");
    }

    private static string TruncateValue(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return value[..maxLength] + "...";
    }

    private static void FormatComparisonProperties(StringBuilder sb, JsonElement result, int id1, int id2)
    {
        // Try common comparison patterns
        string[] comparisonFields = ["method", "url", "statusCode", "durationMs", "process"];

        if (result.TryGetProperty("request1", out var r1) && result.TryGetProperty("request2", out var r2))
        {
            foreach (var field in comparisonFields)
            {
                var has1 = r1.TryGetProperty(field, out var v1);
                var has2 = r2.TryGetProperty(field, out var v2);

                if (!has1 && !has2) continue;

                // Durations read like the app's comparison view ("177 ms", "-" while pending)
                var val1 = field == "durationMs" ? TrafficTools.FormatDuration(r1) : has1 ? FormatDiffValue(v1) : "(missing)";
                var val2 = field == "durationMs" ? TrafficTools.FormatDuration(r2) : has2 ? FormatDiffValue(v2) : "(missing)";

                if (val1 == val2)
                {
                    sb.AppendLine($"[SAME] {field}: {TruncateValue(val1, 200)}");
                }
                else
                {
                    sb.AppendLine($"[DIFFERENT] {field}:");
                    sb.AppendLine($"  Request #{id1}: {TruncateValue(val1, 200)}");
                    sb.AppendLine($"  Request #{id2}: {TruncateValue(val2, 200)}");
                }
                sb.AppendLine();
            }
        }
    }
}
