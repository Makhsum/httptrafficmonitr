using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;

namespace HttpTrafficMonitor.McpServer.Tools;

[McpServerToolType]
public static class ProxyTools
{
    // Offered only while the user allows agent changes in the app (see AgentChangesWatcher)
    internal static readonly string[] ChangeToolNames =
    {
        nameof(start_proxy),
        nameof(stop_proxy),
        nameof(pause_capture),
        nameof(resume_capture)
    };

    [McpServerTool, Description("Get the current status of the HTTP proxy including whether it's running, paused, the listening port, and request count.")]
    public static async Task<string> get_proxy_status(IpcClient client)
    {
        try
        {
            var result = await client.GetAsync("/proxy/status");
            var sb = new StringBuilder();
            sb.AppendLine("=== Proxy Status ===");

            var isRunning = result.TryGetProperty("isRunning", out var runningEl) && runningEl.GetBoolean();
            sb.AppendLine($"State:          {(isRunning ? "Running" : "Stopped")}");

            if (result.TryGetProperty("isPaused", out var pausedEl))
                sb.AppendLine($"Capture:        {(pausedEl.GetBoolean() ? "Paused" : "Active")}");

            if (result.TryGetProperty("port", out var portEl))
                sb.AppendLine($"Port:           {portEl}");

            if (result.TryGetProperty("requestCount", out var countEl))
                sb.AppendLine($"Request Count:  {countEl}");

            if (result.TryGetProperty("uptime", out var uptimeEl))
                sb.AppendLine($"Uptime:         {uptimeEl.GetString()}");

            return sb.ToString().TrimEnd();
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Start the HTTP proxy server to begin capturing traffic. If Windows is asking on the desktop to trust the app's root certificate, this answers at once that a confirmation is waiting; do not call it again, ask the user to answer the prompt and then check get_proxy_status.")]
    public static async Task<string> start_proxy(IpcClient client)
    {
        try
        {
            var result = await client.PostAsync("/proxy/start");

            if (result.TryGetProperty("message", out var msgEl))
                return msgEl.GetString() ?? "Proxy started successfully.";

            return "Proxy started successfully.";
        }
        catch (IpcApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            // The app refuses changes from agents until the user allows them
            return $"The proxy was not started: {ex.ApiError ?? ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Stop the HTTP proxy server. This will stop capturing all traffic.")]
    public static async Task<string> stop_proxy(IpcClient client)
    {
        try
        {
            var result = await client.PostAsync("/proxy/stop");

            if (result.TryGetProperty("message", out var msgEl))
                return msgEl.GetString() ?? "Proxy stopped successfully.";

            return "Proxy stopped successfully.";
        }
        catch (IpcApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            // The app refuses changes from agents until the user allows them
            return $"The proxy was not stopped: {ex.ApiError ?? ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Pause traffic capture. The proxy stays running but new requests are not recorded.")]
    public static async Task<string> pause_capture(IpcClient client)
    {
        try
        {
            var result = await client.PostAsync("/proxy/pause");

            if (result.TryGetProperty("message", out var msgEl))
                return msgEl.GetString() ?? "Capture paused successfully.";

            return "Capture paused successfully.";
        }
        catch (IpcApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            // The app refuses changes from agents until the user allows them
            return $"The capture was not paused: {ex.ApiError ?? ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Resume traffic capture after it was paused.")]
    public static async Task<string> resume_capture(IpcClient client)
    {
        try
        {
            var result = await client.PostAsync("/proxy/resume");

            if (result.TryGetProperty("message", out var msgEl))
                return msgEl.GetString() ?? "Capture resumed successfully.";

            return "Capture resumed successfully.";
        }
        catch (IpcApiException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            // The app refuses changes from agents until the user allows them
            return $"The capture was not resumed: {ex.ApiError ?? ex.Message}";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }
}
