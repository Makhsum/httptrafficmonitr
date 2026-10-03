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
        // Talk to the app directly: while capturing, the system proxy is the app itself, and going
        // through it would show these calls in the grid and fire the user's alert rules
        var handler = new HttpClientHandler { UseProxy = false };
        _http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<JsonElement> GetAsync(string path)
    {
        var response = await _http.GetAsync($"/api{path}");
        await EnsureSuccessAsync(response);
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
    }

    public async Task<string> GetStringAsync(string path)
    {
        var response = await _http.GetAsync($"/api{path}");
        await EnsureSuccessAsync(response);
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
        await EnsureSuccessAsync(response);
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
    }

    public async Task<JsonElement> PutAsync(string path, object body)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
        var response = await _http.PutAsync($"/api{path}", content);
        await EnsureSuccessAsync(response);
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
    }

    public async Task<JsonElement> DeleteAsync(string path)
    {
        var response = await _http.DeleteAsync($"/api{path}");
        await EnsureSuccessAsync(response);
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
    }

    // Same exception as EnsureSuccessStatusCode, plus the {"error": ...} text the app answered with
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        string? apiError = null;
        try
        {
            var json = await response.Content.ReadAsStringAsync();
            var body = JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                apiError = error.GetString();
        }
        catch (JsonException) { }

        throw new IpcApiException(
            $"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).",
            response.StatusCode, apiError);
    }

    public void Dispose() => _http.Dispose();
}

public class IpcApiException : HttpRequestException
{
    public string? ApiError { get; }

    public IpcApiException(string message, System.Net.HttpStatusCode statusCode, string? apiError)
        : base(message, null, statusCode)
    {
        ApiError = apiError;
    }
}
