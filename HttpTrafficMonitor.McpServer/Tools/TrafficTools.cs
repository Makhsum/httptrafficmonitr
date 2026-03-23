using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace HttpTrafficMonitor.McpServer.Tools;

[McpServerToolType]
public static class TrafficTools
{
    [McpServerTool, Description("Get a list of captured HTTP requests with optional filtering by method, domain, process, or search text. Returns a compact summary of each request.")]
    public static async Task<string> get_requests(
        IpcClient client,
        [Description("Number of requests to skip for pagination. Default: 0")] int skip = 0,
        [Description("Number of requests to return. Default: 20")] int take = 20,
        [Description("Filter by HTTP method (e.g. GET, POST)")] string? method = null,
        [Description("Filter by domain name")] string? domain = null,
        [Description("Filter by process name")] string? process = null,
        [Description("Search text to filter requests by URL, headers, or body")] string? search = null,
        [Description("If true, return only bookmarked requests. Default: false")] bool bookmarkedOnly = false)
    {
        try
        {
            var queryParts = new List<string>
            {
                $"skip={skip}",
                $"take={take}"
            };

            if (!string.IsNullOrEmpty(method)) queryParts.Add($"method={Uri.EscapeDataString(method)}");
            if (!string.IsNullOrEmpty(domain)) queryParts.Add($"domain={Uri.EscapeDataString(domain)}");
            if (!string.IsNullOrEmpty(process)) queryParts.Add($"process={Uri.EscapeDataString(process)}");
            if (!string.IsNullOrEmpty(search)) queryParts.Add($"search={Uri.EscapeDataString(search)}");
            if (bookmarkedOnly) queryParts.Add("bookmarkedOnly=true");

            var query = string.Join("&", queryParts);
            var result = await client.GetAsync($"/requests?{query}");

            return FormatRequestList(result);
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Get full details of a specific HTTP request including headers, body, TLS info, WebSocket messages, and timing breakdown.")]
    public static async Task<string> get_request_details(
        IpcClient client,
        [Description("The ID of the request to retrieve")] int requestId)
    {
        try
        {
            var result = await client.GetAsync($"/requests/{requestId}");
            var sb = new StringBuilder();

            sb.AppendLine($"=== Request #{requestId} Details ===");
            sb.AppendLine();

            // Basic info
            if (result.TryGetProperty("method", out var methodEl))
                sb.Append($"{methodEl.GetString()} ");
            if (result.TryGetProperty("url", out var urlEl))
                sb.AppendLine(urlEl.GetString());

            if (result.TryGetProperty("statusCode", out var statusEl))
                sb.AppendLine($"Status:    {statusEl}");
            if (result.TryGetProperty("process", out var processEl))
                sb.AppendLine($"Process:   {processEl.GetString()}");
            if (result.TryGetProperty("timestamp", out var tsEl))
                sb.AppendLine($"Timestamp: {tsEl.GetString()}");
            if (result.TryGetProperty("duration", out var durEl))
                sb.AppendLine($"Duration:  {durEl}ms");
            if (result.TryGetProperty("isBookmarked", out var bmEl))
                sb.AppendLine($"Bookmarked: {bmEl.GetBoolean()}");

            sb.AppendLine();

            // Request headers
            if (result.TryGetProperty("requestHeaders", out var reqHeaders))
            {
                sb.AppendLine("--- Request Headers ---");
                FormatHeaders(sb, reqHeaders);
                sb.AppendLine();
            }

            // Request body
            if (result.TryGetProperty("requestBody", out var reqBody))
            {
                var body = reqBody.GetString();
                if (!string.IsNullOrEmpty(body))
                {
                    sb.AppendLine("--- Request Body ---");
                    sb.AppendLine(TruncateBody(body, 2000));
                    sb.AppendLine();
                }
            }

            // Response headers
            if (result.TryGetProperty("responseHeaders", out var resHeaders))
            {
                sb.AppendLine("--- Response Headers ---");
                FormatHeaders(sb, resHeaders);
                sb.AppendLine();
            }

            // Response body
            if (result.TryGetProperty("responseBody", out var resBody))
            {
                var body = resBody.GetString();
                if (!string.IsNullOrEmpty(body))
                {
                    sb.AppendLine("--- Response Body ---");
                    sb.AppendLine(TruncateBody(body, 2000));
                    sb.AppendLine();
                }
            }

            // TLS info
            if (result.TryGetProperty("tlsInfo", out var tlsInfo) && tlsInfo.ValueKind != JsonValueKind.Null)
            {
                sb.AppendLine("--- TLS Info ---");
                FormatTlsInfo(sb, tlsInfo);
                sb.AppendLine();
            }

            // WebSocket messages
            if (result.TryGetProperty("webSocketMessages", out var wsMessages)
                && wsMessages.ValueKind == JsonValueKind.Array
                && wsMessages.GetArrayLength() > 0)
            {
                sb.AppendLine("--- WebSocket Messages ---");
                FormatWebSocketMessages(sb, wsMessages);
                sb.AppendLine();
            }

            // Timing breakdown
            if (result.TryGetProperty("timing", out var timing) && timing.ValueKind != JsonValueKind.Null)
            {
                sb.AppendLine("--- Timing Breakdown ---");
                FormatTiming(sb, timing);
            }

            return sb.ToString().TrimEnd();
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Clear all captured HTTP requests from the session.")]
    public static async Task<string> clear_requests(IpcClient client)
    {
        try
        {
            var result = await client.DeleteAsync("/requests");

            if (result.TryGetProperty("message", out var msgEl))
                return msgEl.GetString() ?? "All requests cleared.";

            return "All requests cleared.";
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Get TLS/SSL certificate and connection details for a specific request.")]
    public static async Task<string> get_request_tls_info(
        IpcClient client,
        [Description("The ID of the request")] int requestId)
    {
        try
        {
            var result = await client.GetAsync($"/requests/{requestId}/tls");
            var sb = new StringBuilder();

            sb.AppendLine($"=== TLS Info for Request #{requestId} ===");
            sb.AppendLine();
            FormatTlsInfo(sb, result);

            return sb.ToString().TrimEnd();
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Get WebSocket messages exchanged during a specific request.")]
    public static async Task<string> get_websocket_messages(
        IpcClient client,
        [Description("The ID of the request")] int requestId)
    {
        try
        {
            var result = await client.GetAsync($"/requests/{requestId}/websocket");
            var sb = new StringBuilder();

            sb.AppendLine($"=== WebSocket Messages for Request #{requestId} ===");
            sb.AppendLine();

            if (result.ValueKind == JsonValueKind.Array)
            {
                FormatWebSocketMessages(sb, result);
            }
            else if (result.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                FormatWebSocketMessages(sb, messages);
            }
            else
            {
                sb.AppendLine("No WebSocket messages found.");
            }

            return sb.ToString().TrimEnd();
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    // --- Shared formatting helpers ---

    internal static string FormatRequestList(JsonElement result)
    {
        var sb = new StringBuilder();

        JsonElement items;
        int totalCount = 0;

        if (result.TryGetProperty("requests", out var itemsEl))
        {
            items = itemsEl;
            if (result.TryGetProperty("totalCount", out var tcEl))
                totalCount = tcEl.GetInt32();
        }
        else if (result.ValueKind == JsonValueKind.Array)
        {
            items = result;
            totalCount = result.GetArrayLength();
        }
        else
        {
            return "No requests found.";
        }

        if (items.GetArrayLength() == 0)
        {
            sb.AppendLine("No requests found.");
            sb.AppendLine($"Total: {totalCount}");
            return sb.ToString().TrimEnd();
        }

        foreach (var req in items.EnumerateArray())
        {
            var id = req.TryGetProperty("id", out var idEl) ? idEl.ToString() : "?";
            var ts = req.TryGetProperty("timestamp", out var tsEl) ? FormatTimestamp(tsEl.GetString()) : "?";
            var method = req.TryGetProperty("method", out var mEl) ? mEl.GetString() : "?";
            var status = req.TryGetProperty("statusCode", out var sEl) ? sEl.ToString() : "?";
            var url = req.TryGetProperty("url", out var uEl) ? uEl.GetString() : "?";
            var duration = req.TryGetProperty("duration", out var dEl) ? $"{dEl}ms" : "?";
            var proc = req.TryGetProperty("process", out var pEl) ? pEl.GetString() : "?";

            // Truncate URL if too long
            if (url != null && url.Length > 80)
                url = url[..77] + "...";

            sb.AppendLine($"[{id}] {ts} {method} {status} {url} ({duration}) [{proc}]");
        }

        sb.AppendLine();
        sb.AppendLine($"Total: {totalCount}");
        return sb.ToString().TrimEnd();
    }

    private static string FormatTimestamp(string? timestamp)
    {
        if (string.IsNullOrEmpty(timestamp)) return "?";
        if (DateTime.TryParse(timestamp, out var dt))
            return dt.ToString("HH:mm:ss.fff");
        return timestamp;
    }

    private static void FormatHeaders(StringBuilder sb, JsonElement headers)
    {
        if (headers.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in headers.EnumerateObject())
            {
                sb.AppendLine($"  {prop.Name}: {prop.Value}");
            }
        }
        else if (headers.ValueKind == JsonValueKind.Array)
        {
            foreach (var header in headers.EnumerateArray())
            {
                var name = header.TryGetProperty("name", out var nEl) ? nEl.GetString() : "?";
                var value = header.TryGetProperty("value", out var vEl) ? vEl.GetString() : "?";
                sb.AppendLine($"  {name}: {value}");
            }
        }
        else if (headers.ValueKind == JsonValueKind.String)
        {
            sb.AppendLine($"  {headers.GetString()}");
        }
    }

    private static string TruncateBody(string body, int maxLength)
    {
        if (body.Length <= maxLength) return body;
        return body[..maxLength] + $"\n... (truncated, {body.Length} total chars)";
    }

    private static void FormatTlsInfo(StringBuilder sb, JsonElement tls)
    {
        if (tls.ValueKind == JsonValueKind.Null)
        {
            sb.AppendLine("No TLS information available.");
            return;
        }

        if (tls.TryGetProperty("protocol", out var protoEl))
            sb.AppendLine($"Protocol:       {protoEl.GetString()}");
        if (tls.TryGetProperty("cipherSuite", out var cipherEl))
            sb.AppendLine($"Cipher Suite:   {cipherEl.GetString()}");
        if (tls.TryGetProperty("serverCertificate", out var certEl) && certEl.ValueKind != JsonValueKind.Null)
        {
            sb.AppendLine("Server Certificate:");
            if (certEl.TryGetProperty("subject", out var subEl))
                sb.AppendLine($"  Subject:      {subEl.GetString()}");
            if (certEl.TryGetProperty("issuer", out var issEl))
                sb.AppendLine($"  Issuer:       {issEl.GetString()}");
            if (certEl.TryGetProperty("notBefore", out var nbEl))
                sb.AppendLine($"  Valid From:   {nbEl.GetString()}");
            if (certEl.TryGetProperty("notAfter", out var naEl))
                sb.AppendLine($"  Valid Until:  {naEl.GetString()}");
            if (certEl.TryGetProperty("thumbprint", out var thEl))
                sb.AppendLine($"  Thumbprint:   {thEl.GetString()}");
            if (certEl.TryGetProperty("serialNumber", out var snEl))
                sb.AppendLine($"  Serial:       {snEl.GetString()}");
        }

        // Handle flat TLS properties (if certificate info is at the top level)
        if (tls.TryGetProperty("subject", out var subFlat))
            sb.AppendLine($"Subject:        {subFlat.GetString()}");
        if (tls.TryGetProperty("issuer", out var issFlat))
            sb.AppendLine($"Issuer:         {issFlat.GetString()}");
        if (tls.TryGetProperty("notBefore", out var nbFlat))
            sb.AppendLine($"Valid From:     {nbFlat.GetString()}");
        if (tls.TryGetProperty("notAfter", out var naFlat))
            sb.AppendLine($"Valid Until:    {naFlat.GetString()}");
        if (tls.TryGetProperty("thumbprint", out var thFlat))
            sb.AppendLine($"Thumbprint:     {thFlat.GetString()}");
    }

    private static void FormatWebSocketMessages(StringBuilder sb, JsonElement messages)
    {
        int count = 0;
        foreach (var msg in messages.EnumerateArray())
        {
            count++;
            var direction = msg.TryGetProperty("direction", out var dirEl) ? dirEl.GetString() : "?";
            var type = msg.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : "?";
            var timestamp = msg.TryGetProperty("timestamp", out var tsEl) ? FormatTimestamp(tsEl.GetString()) : "?";
            var data = msg.TryGetProperty("data", out var dataEl) ? dataEl.GetString() : "";
            var length = msg.TryGetProperty("length", out var lenEl) ? lenEl.ToString() : "?";

            var arrow = direction?.Equals("send", StringComparison.OrdinalIgnoreCase) == true ? ">>" : "<<";

            sb.AppendLine($"  {arrow} [{timestamp}] ({type}, {length} bytes)");
            if (!string.IsNullOrEmpty(data))
                sb.AppendLine($"     {TruncateBody(data, 200)}");
        }

        if (count == 0)
            sb.AppendLine("No WebSocket messages found.");
        else
            sb.AppendLine($"Total messages: {count}");
    }

    private static void FormatTiming(StringBuilder sb, JsonElement timing)
    {
        foreach (var prop in timing.EnumerateObject())
        {
            sb.AppendLine($"  {prop.Name}: {prop.Value}ms");
        }
    }
}
