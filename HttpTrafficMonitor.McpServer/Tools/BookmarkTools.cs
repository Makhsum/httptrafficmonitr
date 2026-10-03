using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace HttpTrafficMonitor.McpServer.Tools;

[McpServerToolType]
public static class BookmarkTools
{
    [McpServerTool, Description("Toggle the bookmark state of an HTTP request, optionally adding notes")]
    public static async Task<string> toggle_bookmark(
        IpcClient client,
        [Description("The ID of the request to bookmark or unbookmark")] int requestId,
        [Description("Optional notes to attach to the bookmark")] string notes = "")
    {
        try
        {
            var result = await client.PostAsync($"/bookmarks/{requestId}/toggle", new { notes });

            var isBookmarked = result.TryGetProperty("isBookmarked", out var bookmarkedEl)
                && bookmarkedEl.GetBoolean();

            return isBookmarked
                ? $"Request {requestId} is now bookmarked."
                : $"Request {requestId} bookmark removed.";
        }
        catch (HttpRequestException ex)
        {
            return $"Error toggling bookmark: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }

    [McpServerTool, Description("Get all bookmarked HTTP requests")]
    public static async Task<string> get_bookmarks(
        IpcClient client)
    {
        try
        {
            // The app pages this list (50 by default); ask for all of them at once
            var result = await client.GetAsync($"/bookmarks?take={int.MaxValue}");

            if (!result.TryGetProperty("requests", out var requestsEl)
                || requestsEl.ValueKind != JsonValueKind.Array || requestsEl.GetArrayLength() == 0)
            {
                return "No bookmarked requests found.";
            }

            var lines = new List<string>();
            var count = 0;

            foreach (var bookmark in requestsEl.EnumerateArray())
            {
                var id = bookmark.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
                var method = bookmark.TryGetProperty("method", out var methodEl)
                    ? methodEl.GetString() ?? "?"
                    : "?";
                var url = bookmark.TryGetProperty("url", out var urlEl)
                    ? urlEl.GetString() ?? "?"
                    : "?";
                var bookmarkNotes = bookmark.TryGetProperty("bookmarkNotes", out var notesEl)
                    ? notesEl.GetString() ?? ""
                    : "";

                var line = $"[{id}] {method} {url}";
                if (!string.IsNullOrEmpty(bookmarkNotes))
                {
                    line += $" - Notes: {bookmarkNotes}";
                }

                lines.Add(line);
                count++;
            }

            lines.Insert(0, $"Bookmarked requests ({count} total):");
            return string.Join("\n", lines);
        }
        catch (HttpRequestException ex)
        {
            return $"Error retrieving bookmarks: {ex.Message}. Make sure HttpTrafficMonitor is running.";
        }
    }
}
