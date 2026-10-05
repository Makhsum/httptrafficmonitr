using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Text.Json;

namespace HttpTrafficMonitor.McpServer.Tools;

[McpServerToolType]
public static class ExportTools
{
    [McpServerTool, Description("Export an HTTP request as a curl command.")]
    public static async Task<string> export_as_curl(
        IpcClient client,
        [Description("The ID of the request to export as curl")] int requestId)
    {
        try
        {
            // The app sends the command as {curl}
            var result = await client.GetAsync($"/export/curl/{requestId}");
            return TrafficTools.WithCredentialsNotice($"=== curl Command ===\n\n{GetString(result, "curl")}", result);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return $"Request #{requestId} not found.";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Export HTTP requests in HAR (HTTP Archive) format. Exports all captured requests if no IDs are specified.")]
    public static async Task<string> export_as_har(
        IpcClient client,
        [Description("Comma-separated list of request IDs to export (leave empty to export all)")] string? requestIds = null)
    {
        try
        {
            var ids = ParseRequestIds(requestIds);
            var result = await client.PostAsync("/export/har", new { requestIds = ids });
            return FormatJsonResult("HAR Export", result);
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Export HTTP requests as a Postman collection.")]
    public static async Task<string> export_as_postman(
        IpcClient client,
        [Description("Comma-separated list of request IDs to export (leave empty to export all)")] string? requestIds = null,
        [Description("Name for the Postman collection")] string collectionName = "HttpTrafficMonitor Export")
    {
        try
        {
            var ids = ParseRequestIds(requestIds);
            var result = await client.PostAsync("/export/postman", new { requestIds = ids, collectionName });
            return FormatJsonResult("Postman Collection", result);
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Export HTTP requests as JSON.")]
    public static async Task<string> export_as_json(
        IpcClient client,
        [Description("Comma-separated list of request IDs to export (leave empty to export all)")] string? requestIds = null)
    {
        try
        {
            var ids = ParseRequestIds(requestIds);
            var result = await client.PostAsync("/export/json", new { requestIds = ids });
            return FormatJsonResult("JSON Export", result);
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Export HTTP requests as CSV.")]
    public static async Task<string> export_as_csv(
        IpcClient client,
        [Description("Comma-separated list of request IDs to export (leave empty to export all)")] string? requestIds = null)
    {
        try
        {
            var ids = ParseRequestIds(requestIds);
            var result = await client.PostAsync("/export/csv", new { requestIds = ids });

            // The app sends the CSV text as {csv}
            return TrafficTools.WithCredentialsNotice($"=== CSV Export ===\n\n{GetString(result, "csv").TrimEnd()}", result);
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    private static int[]? ParseRequestIds(string? requestIds)
    {
        if (string.IsNullOrWhiteSpace(requestIds))
            return null;

        var parts = requestIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = new List<int>();

        foreach (var part in parts)
        {
            if (int.TryParse(part, out var id))
            {
                ids.Add(id);
            }
        }

        return ids.Count > 0 ? ids.ToArray() : null;
    }

    private static string FormatJsonResult(string title, JsonElement result)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(result, options);
        return $"=== {title} ===\n\n{json}";
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString() ?? "",
                JsonValueKind.Null => "",
                _ => prop.ToString()
            };
        }
        return "";
    }
}
