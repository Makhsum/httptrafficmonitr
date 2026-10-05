using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace HttpTrafficMonitor.McpServer.Tools;

[McpServerToolType]
public static class ReplayTools
{
    [McpServerTool, Description("Replay an HTTP request with the specified method, URL, headers, and body. Returns the response status, duration, headers, and body.")]
    public static async Task<string> replay_request(
        IpcClient client,
        [Description("HTTP method (e.g., GET, POST, PUT, DELETE)")] string method,
        [Description("Full URL to send the request to")] string url,
        [Description("Request headers as 'Key: Value' lines or a JSON object (e.g., {\"Content-Type\": \"application/json\"})")] string? headers = null,
        [Description("Request body content")] string? body = null)
    {
        try
        {
            var parsedHeaders = ParseHeaders(headers);

            var requestBody = new Dictionary<string, object?>
            {
                ["method"] = method,
                ["url"] = url
            };

            if (parsedHeaders != null)
                requestBody["headers"] = parsedHeaders;

            if (body != null)
                requestBody["body"] = body;

            var result = await client.PostAsync("/replay", requestBody);

            var sb = new StringBuilder();
            sb.AppendLine("=== Replay Response ===");
            sb.AppendLine();

            var statusCode = GetString(result, "statusCode");
            var statusDescription = GetString(result, "statusDescription");
            if (!string.IsNullOrEmpty(statusDescription))
                sb.AppendLine($"Status:   {statusCode} {statusDescription}");
            else
                sb.AppendLine($"Status:   {statusCode}");

            var duration = GetString(result, "duration");
            if (string.IsNullOrEmpty(duration))
                duration = GetString(result, "durationMs");
            if (!string.IsNullOrEmpty(duration))
                sb.AppendLine($"Duration: {duration}ms");

            sb.AppendLine();

            // Response headers
            if (result.TryGetProperty("responseHeaders", out var respHeaders))
            {
                sb.AppendLine("Response Headers:");
                if (respHeaders.ValueKind == JsonValueKind.Object)
                {
                    foreach (var header in respHeaders.EnumerateObject())
                    {
                        sb.AppendLine($"  {header.Name}: {header.Value}");
                    }
                }
                else if (respHeaders.ValueKind == JsonValueKind.Array)
                {
                    foreach (var header in respHeaders.EnumerateArray())
                    {
                        var name = GetString(header, "name");
                        var value = GetString(header, "value");
                        sb.AppendLine($"  {name}: {value}");
                    }
                }
                else if (respHeaders.ValueKind == JsonValueKind.String)
                {
                    // The app sends the header block as "Name: Value" lines
                    foreach (var line in (respHeaders.GetString() ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        sb.AppendLine($"  {line.TrimEnd('\r')}");
                    }
                }
                sb.AppendLine();
            }
            else if (result.TryGetProperty("headers", out var hdrs))
            {
                sb.AppendLine("Response Headers:");
                if (hdrs.ValueKind == JsonValueKind.Object)
                {
                    foreach (var header in hdrs.EnumerateObject())
                    {
                        sb.AppendLine($"  {header.Name}: {header.Value}");
                    }
                }
                sb.AppendLine();
            }

            // Response body
            var responseBody = GetString(result, "responseBody");
            if (string.IsNullOrEmpty(responseBody))
                responseBody = GetString(result, "body");

            if (!string.IsNullOrEmpty(responseBody))
            {
                sb.AppendLine("Response Body:");
                if (responseBody.Length > 2000)
                {
                    sb.AppendLine(responseBody[..2000]);
                    sb.AppendLine($"... (truncated, {responseBody.Length} total characters)");
                }
                else
                {
                    sb.AppendLine(responseBody);
                }
            }

            return TrafficTools.WithCredentialsNotice(sb.ToString().TrimEnd(), result);
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    private static Dictionary<string, string>? ParseHeaders(string? headers)
    {
        if (string.IsNullOrWhiteSpace(headers))
            return null;

        var trimmed = headers.Trim();

        // Try parsing as JSON object
        if (trimmed.StartsWith('{'))
        {
            try
            {
                var jsonHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(trimmed);
                if (jsonHeaders != null && jsonHeaders.Count > 0)
                    return jsonHeaders;
            }
            catch (JsonException)
            {
                // Fall through to line-by-line parsing
            }
        }

        // Parse as "Key: Value" lines
        var result = new Dictionary<string, string>();
        var lines = trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine))
                continue;

            var colonIndex = trimmedLine.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = trimmedLine[..colonIndex].Trim();
                var value = trimmedLine[(colonIndex + 1)..].Trim();
                result[key] = value;
            }
        }

        return result.Count > 0 ? result : null;
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
