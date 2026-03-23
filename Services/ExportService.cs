using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HttpTrafficMonitor.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HttpTrafficMonitor.Services
{
    public static class ExportService
    {
        public static string ToCurl(HttpRequestEntry entry)
        {
            var sb = new StringBuilder();
            sb.Append($"curl -X {entry.Method} '{entry.Url}'");

            var headers = HttpReplayService.ParseHeaders(entry.RequestHeaders);
            foreach (var (key, value) in headers)
            {
                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                sb.Append($" \\\n  -H '{key}: {value}'");
            }

            if (!string.IsNullOrEmpty(entry.RequestBody) && entry.Method != "GET" && entry.Method != "HEAD")
            {
                string escaped = entry.RequestBody.Replace("'", "'\\''");
                sb.Append($" \\\n  -d '{escaped}'");
            }

            return sb.ToString();
        }

        public static string ToPostmanCollection(IEnumerable<HttpRequestEntry> entries, string collectionName = "Exported Collection")
        {
            var items = entries.Select(e =>
            {
                var headers = HttpReplayService.ParseHeaders(e.RequestHeaders);
                var headerArray = headers.Select(h => new { key = h.Key, value = h.Value }).ToArray();

                Uri uri;
                try { uri = new Uri(e.Url); }
                catch { uri = new Uri("http://unknown"); }

                var item = new
                {
                    name = $"{e.Method} {uri.PathAndQuery}",
                    request = new
                    {
                        method = e.Method,
                        header = headerArray,
                        url = new
                        {
                            raw = e.Url,
                            protocol = uri.Scheme,
                            host = uri.Host.Split('.'),
                            path = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries),
                            query = string.IsNullOrEmpty(uri.Query) ? Array.Empty<object>()
                                : uri.Query.TrimStart('?').Split('&').Select(q =>
                                {
                                    var parts = q.Split('=', 2);
                                    return new { key = parts[0], value = parts.Length > 1 ? parts[1] : "" };
                                }).ToArray<object>()
                        },
                        body = string.IsNullOrEmpty(e.RequestBody) ? null : new
                        {
                            mode = "raw",
                            raw = e.RequestBody
                        }
                    },
                    response = Array.Empty<object>()
                };
                return item;
            }).ToArray();

            var collection = new
            {
                info = new
                {
                    name = collectionName,
                    schema = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
                },
                item = items
            };

            return JsonConvert.SerializeObject(collection, Formatting.Indented);
        }

        public static string ToHar(IEnumerable<HttpRequestEntry> entries)
        {
            var entriesList = entries.Select(e =>
            {
                var reqHeaders = HttpReplayService.ParseHeaders(e.RequestHeaders)
                    .Select(h => new { name = h.Key, value = h.Value }).ToArray();

                var respHeaders = e.ResponseHeaders != null
                    ? HttpReplayService.ParseHeaders(e.ResponseHeaders)
                        .Select(h => new { name = h.Key, value = h.Value }).ToArray()
                    : Array.Empty<object>();

                return new
                {
                    startedDateTime = e.Timestamp.ToString("O"),
                    time = e.Duration?.TotalMilliseconds ?? 0,
                    request = new
                    {
                        method = e.Method,
                        url = e.Url,
                        httpVersion = "HTTP/1.1",
                        headers = reqHeaders,
                        queryString = Array.Empty<object>(),
                        headersSize = -1,
                        bodySize = e.RequestBody?.Length ?? 0,
                        postData = string.IsNullOrEmpty(e.RequestBody) ? null : new
                        {
                            mimeType = e.RequestContentType,
                            text = e.RequestBody
                        }
                    },
                    response = new
                    {
                        status = e.StatusCode ?? 0,
                        statusText = "",
                        httpVersion = "HTTP/1.1",
                        headers = respHeaders,
                        content = new
                        {
                            size = e.ResponseSize ?? 0,
                            mimeType = e.ResponseContentType ?? "",
                            text = e.ResponseBody ?? ""
                        },
                        headersSize = -1,
                        bodySize = e.ResponseSize ?? 0
                    },
                    cache = new { },
                    timings = new
                    {
                        send = 0,
                        wait = e.Duration?.TotalMilliseconds ?? 0,
                        receive = 0
                    }
                };
            }).ToArray();

            var har = new
            {
                log = new
                {
                    version = "1.2",
                    creator = new { name = "HttpTrafficMonitor", version = "1.0" },
                    entries = entriesList
                }
            };

            return JsonConvert.SerializeObject(har, Formatting.Indented);
        }
    }
}
