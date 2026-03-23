using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace HttpTrafficMonitor.McpServer.Tools;

[McpServerToolType]
public static class ContentTools
{
    [McpServerTool, Description("Decode content using the specified encoding (base64, url, gzip, deflate, brotli, or auto-detect)")]
    public static async Task<string> decode_content(
        IpcClient client,
        [Description("The encoded content string to decode")] string content,
        [Description("Encoding type: base64, url, gzip, deflate, brotli, or auto for auto-detection")] string encoding = "auto")
    {
        try
        {
            var result = await client.PostAsync("/content/decode", new { content, encoding });

            var decoded = result.TryGetProperty("decoded", out var decodedEl)
                ? decodedEl.GetString() ?? ""
                : "";

            var detectedEncoding = result.TryGetProperty("encoding", out var encEl)
                ? encEl.GetString() ?? encoding
                : encoding;

            return $"Encoding detected/applied: {detectedEncoding}\n\nDecoded content:\n{decoded}";
        }
        catch (HttpRequestException ex)
        {
            return $"Error decoding content: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Encode content using the specified encoding (base64 or url)")]
    public static async Task<string> encode_content(
        IpcClient client,
        [Description("The content string to encode")] string content,
        [Description("Encoding type: base64 or url")] string encoding)
    {
        try
        {
            var result = await client.PostAsync("/content/encode", new { content, encoding });

            var encoded = result.TryGetProperty("encoded", out var encodedEl)
                ? encodedEl.GetString() ?? ""
                : "";

            return $"Encoding applied: {encoding}\n\nEncoded content:\n{encoded}";
        }
        catch (HttpRequestException ex)
        {
            return $"Error encoding content: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }
}
