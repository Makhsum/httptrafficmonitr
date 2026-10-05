using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Text.Json;

namespace HttpTrafficMonitor.McpServer.Tools;

[McpServerToolType]
public static class SessionTools
{
    [McpServerTool, Description("Save the current HTTP traffic session to an .hts file")]
    public static async Task<string> save_session(
        IpcClient client,
        [Description("Full path to the .hts file to save")] string filePath,
        [Description("Optional description for the session")] string description = "")
    {
        try
        {
            var result = await client.PostAsync("/sessions/save", new { filePath, description });
            return TrafficTools.WithCredentialsNotice($"Session saved successfully to: {filePath}", result);
        }
        catch (HttpRequestException ex)
        {
            return $"Error saving session: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Load an HTTP traffic session from an .hts file")]
    public static async Task<string> load_session(
        IpcClient client,
        [Description("Full path to the .hts file to load")] string filePath)
    {
        try
        {
            var result = await client.PostAsync("/sessions/load", new { filePath });

            var requestCount = result.TryGetProperty("requestCount", out var countEl)
                ? countEl.GetInt32()
                : 0;

            return $"Session loaded successfully from: {filePath}\nRequests loaded: {requestCount}";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return $"Session file not found: {filePath}";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return $"'{filePath}' could not be read as a session. Load a .hts file saved by HttpTrafficMonitor.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return $"Session file cannot be opened: {filePath}. Another program may hold it open, or you lack permission to read it.";
        }
        catch (HttpRequestException ex)
        {
            return $"Error loading session: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("List recently opened/saved session files")]
    public static async Task<string> list_recent_sessions(
        IpcClient client)
    {
        try
        {
            var result = await client.GetAsync("/sessions/recent");

            if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
            {
                return "No recent sessions found.";
            }

            var lines = new List<string> { "Recent sessions:" };
            var index = 1;

            foreach (var session in result.EnumerateArray())
            {
                var path = session.GetString() ?? "(unknown)";
                lines.Add($"  {index}. {path}");
                index++;
            }

            return string.Join("\n", lines);
        }
        catch (HttpRequestException ex)
        {
            return $"Error listing recent sessions: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }
}
