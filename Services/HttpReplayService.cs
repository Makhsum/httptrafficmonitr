using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HttpTrafficMonitor.Models;

namespace HttpTrafficMonitor.Services
{
    public class HttpReplayService : IDisposable
    {
        private readonly HttpClient _client;

        public HttpReplayService()
        {
            var handler = new HttpClientHandler
            {
                UseProxy = false,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = false
            };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task<ReplayResult> SendRequestAsync(string method, string url,
            Dictionary<string, string> headers, string? body)
        {
            var result = new ReplayResult { RequestTimestamp = DateTime.Now };
            var sw = Stopwatch.StartNew();

            try
            {
                var request = new HttpRequestMessage(new HttpMethod(method), url);

                if (body != null && method != "GET" && method != "HEAD")
                {
                    string contentType = headers.GetValueOrDefault("Content-Type", "application/octet-stream");
                    request.Content = new StringContent(body, Encoding.UTF8);
                    request.Content.Headers.Clear();
                    request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }

                foreach (var (key, value) in headers)
                {
                    if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                    if (key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                    if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                    if (key.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                    if (key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                    if (key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
                    request.Headers.TryAddWithoutValidation(key, value);
                }

                var response = await _client.SendAsync(request);
                sw.Stop();

                result.StatusCode = (int)response.StatusCode;
                result.Duration = sw.Elapsed;
                result.ResponseTimestamp = DateTime.Now;

                var headerSb = new StringBuilder();
                headerSb.AppendLine($"HTTP/{response.Version} {(int)response.StatusCode} {response.ReasonPhrase}");
                foreach (var h in response.Headers.Concat(response.Content.Headers))
                    headerSb.AppendLine($"{h.Key}: {string.Join(", ", h.Value)}");
                result.ResponseHeaders = headerSb.ToString().TrimEnd();

                byte[] bodyBytes = await response.Content.ReadAsByteArrayAsync();
                result.ResponseSize = bodyBytes.Length;
                result.ResponseBody = Encoding.UTF8.GetString(bodyBytes);
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.Duration = sw.Elapsed;
                result.Error = ex.Message;
                result.StatusCode = 0;
            }

            return result;
        }

        public static Dictionary<string, string> ParseHeaders(string headersText)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(headersText)) return dict;

            foreach (string line in headersText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("GET ") || trimmed.StartsWith("POST ") ||
                    trimmed.StartsWith("PUT ") || trimmed.StartsWith("DELETE ") ||
                    trimmed.StartsWith("HTTP/")) continue;

                int colonIdx = trimmed.IndexOf(':');
                if (colonIdx <= 0) continue;

                string key = trimmed[..colonIdx].Trim();
                string value = trimmed[(colonIdx + 1)..].Trim();
                dict[key] = value;
            }
            return dict;
        }

        public void Dispose()
        {
            _client.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public class ReplayResult
    {
        public DateTime RequestTimestamp { get; set; }
        public DateTime ResponseTimestamp { get; set; }
        public int StatusCode { get; set; }
        public string ResponseHeaders { get; set; } = string.Empty;
        public string ResponseBody { get; set; } = string.Empty;
        public long ResponseSize { get; set; }
        public TimeSpan Duration { get; set; }
        public string? Error { get; set; }
        public bool IsSuccess => Error == null && StatusCode > 0;
        public string DurationFormatted => Duration.TotalMilliseconds < 1000
            ? $"{Duration.TotalMilliseconds:F0} ms" : $"{Duration.TotalSeconds:F2} s";
    }
}
