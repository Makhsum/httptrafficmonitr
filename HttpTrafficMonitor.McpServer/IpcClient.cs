using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace HttpTrafficMonitor.McpServer;

public class IpcClient : IDisposable
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public IpcClient()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HTM_API_URL") ?? "http://localhost:18081";
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<JsonElement> GetAsync(string path)
    {
        var response = await _http.GetAsync($"/api{path}");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
    }

    public async Task<string> GetStringAsync(string path)
    {
        var response = await _http.GetAsync($"/api{path}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<JsonElement> PostAsync(string path, object? body = null)
    {
        HttpResponseMessage response;
        if (body != null)
        {
            var content = new StringContent(
                JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            response = await _http.PostAsync($"/api{path}", content);
        }
        else
        {
            response = await _http.PostAsync($"/api{path}", null);
        }
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
    }

    public async Task<JsonElement> PutAsync(string path, object body)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
        var response = await _http.PutAsync($"/api{path}", content);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
    }

    public async Task<JsonElement> DeleteAsync(string path)
    {
        var response = await _http.DeleteAsync($"/api{path}");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
    }

    public void Dispose() => _http.Dispose();
}
