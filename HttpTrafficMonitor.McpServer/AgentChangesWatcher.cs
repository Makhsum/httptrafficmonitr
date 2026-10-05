using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using System.Text.Json;
using HttpTrafficMonitor.McpServer.Tools;

namespace HttpTrafficMonitor.McpServer;

// Offers the tools that change the Auto-Responder only while the user allows agent changes in the app.
// The app refuses those changes itself as well; hiding the tools keeps an agent from trying them at all.
public class AgentChangesWatcher : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IpcClient _client;
    private readonly McpServerPrimitiveCollection<McpServerTool> _tools;
    private readonly List<McpServerTool> _changeTools;

    public AgentChangesWatcher(IpcClient client, IOptions<McpServerOptions> options)
    {
        _client = client;
        var toolsCapability = options.Value.Capabilities?.Tools
            ?? throw new InvalidOperationException("The MCP server has no tools capability.");
        _tools = toolsCapability.ToolCollection
            ?? throw new InvalidOperationException("The MCP server has no tool collection.");

        // The SDK answers initialize with these options when the server has no prompts, so say here that
        // the tool list changes; without it a client has no reason to listen for list_changed
        toolsCapability.ListChanged = true;

        // Settled before the MCP server starts, so the client's first tool list is already the right one
        _changeTools = _tools.Where(t => AutoResponderTools.ChangeToolNames.Contains(t.ProtocolTool.Name)).ToList();
        if (!IsAllowedAsync().GetAwaiter().GetResult())
        {
            foreach (var tool in _changeTools)
                _tools.Remove(tool);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Adding or removing a tool sends notifications/tools/list_changed, so the client refreshes its list
            bool allowed = await IsAllowedAsync();
            foreach (var tool in _changeTools)
            {
                if (allowed)
                    _tools.TryAdd(tool);
                else
                    _tools.Remove(tool);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // An app that cannot be reached, or one too old to know the setting, allows no changes
    private async Task<bool> IsAllowedAsync()
    {
        try
        {
            var result = await _client.GetAsync("/agent-changes");
            return result.TryGetProperty("allowed", out var allowed) && allowed.ValueKind == JsonValueKind.True;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }
}
