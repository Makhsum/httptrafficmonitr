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
            // curl -X HEAD waits for a body that never comes; -I sends a real HEAD request
            sb.Append(entry.Method == "HEAD"
                ? $"curl -I {ShellQuote(entry.Url)}"
                : $"curl -X {entry.Method} {ShellQuote(entry.Url)}");

            var headers = HttpReplayService.ParseHeaderList(entry.RequestHeaders);
            foreach (var (key, value) in headers)
            {
                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                // curl sets the length of the body it sends; the captured one may not match the formatted body
                if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                sb.Append($" \\\n  -H {ShellQuote($"{key}: {value}")}");
            }

            // Without --compressed curl prints a gzip/deflate/br response as raw bytes
            if (headers.Any(h => h.Key.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)))
                sb.Append(" \\\n  --compressed");

            if (!string.IsNullOrEmpty(entry.RequestBody) && entry.Method != "GET" && entry.Method != "HEAD")
                sb.Append($" \\\n  -d {ShellQuote(entry.RequestBody)}");

            return sb.ToString();
        }

        public static string ToCsv(IEnumerable<HttpRequestEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Id,Timestamp,Process,PID,Method,URL,Host,StatusCode,ResponseSize,Duration(ms)");
            foreach (var e in entries)
                sb.AppendLine($"{e.Id},{e.Timestamp:O},{CsvQuote(e.ProcessName)},{e.ProcessId},{e.Method},{CsvQuote(e.Url)},{CsvQuote(e.Host)},{e.StatusCode},{e.ResponseSize},{e.Duration?.TotalMilliseconds:F0}");
            return sb.ToString();
        }

        private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''")}'";

        // A quote inside a quoted CSV field is written twice, or it ends the field early
        private static string CsvQuote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

        public static string ToPostmanCollection(IEnumerable<HttpRequestEntry> entries, string collectionName = "Exported Collection")
        {
            var items = entries.Select(e =>
            {
                var headers = HttpReplayService.ParseHeaderList(e.RequestHeaders);
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
                var reqHeaders = HttpReplayService.ParseHeaderList(e.RequestHeaders)
                    .Select(h => new { name = h.Key, value = h.Value }).ToArray();

                var respHeaderDict = HttpReplayService.ParseHeaders(e.ResponseHeaders ?? "");
                var respHeaders = respHeaderDict
                    .Select(h => new { name = h.Key, value = h.Value }).ToArray();

                // HAR marks a phase that was not measured with -1 and counts the TLS handshake inside connect
                double dns = e.DnsLookupMs ?? -1;
                double ssl = e.TlsHandshakeMs ?? -1;
                double connect = e.TcpConnectMs == null && e.TlsHandshakeMs == null
                    ? -1 : (e.TcpConnectMs ?? 0) + (e.TlsHandshakeMs ?? 0);

                // send, wait and receive may not be -1; without a breakdown the whole duration stays in wait
                bool hasBreakdown = e.TimeToFirstByteMs != null;
                double wait = e.TimeToFirstByteMs ?? e.Duration?.TotalMilliseconds ?? 0;
                double receive = e.ContentDownloadMs ?? 0;

                // time is the sum of the timings that were measured
                double time = hasBreakdown
                    ? Math.Max(dns, 0) + Math.Max(connect, 0) + wait + receive
                    : e.Duration?.TotalMilliseconds ?? 0;

                return new
                {
                    startedDateTime = e.Timestamp.ToString("O"),
                    time,
                    request = new
                    {
                        method = e.Method,
                        url = e.Url,
                        httpVersion = "HTTP/1.1",
                        // cookies and redirectURL are required by HAR 1.2; stricter viewers refuse an entry without them
                        cookies = Array.Empty<object>(),
                        headers = reqHeaders,
                        queryString = Array.Empty<object>(),
                        headersSize = -1,
                        bodySize = e.RequestBody?.Length ?? 0,
                        postData = string.IsNullOrEmpty(e.RequestBody) ? null : new
                        {
                            mimeType = e.RequestContentType ?? "",
                            text = e.RequestBody
                        }
                    },
                    response = new
                    {
                        status = e.StatusCode ?? 0,
                        statusText = "",
                        httpVersion = "HTTP/1.1",
                        cookies = Array.Empty<object>(),
                        headers = respHeaders,
                        content = new
                        {
                            size = e.ResponseSize ?? 0,
                            mimeType = e.ResponseContentType ?? "",
                            text = e.ResponseBody ?? ""
                        },
                        redirectURL = respHeaderDict.TryGetValue("Location", out var location) ? location : "",
                        headersSize = -1,
                        bodySize = e.ResponseSize ?? 0
                    },
                    cache = new { },
                    timings = new
                    {
                        blocked = -1,
                        dns,
                        connect,
                        send = 0,
                        wait,
                        receive,
                        ssl
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

            // HAR leaves out a request's postData when there is none; null is not a valid postData
            return JsonConvert.SerializeObject(har, Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }
    }
}
