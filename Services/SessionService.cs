using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HttpTrafficMonitor.Models;
using Newtonsoft.Json;

namespace HttpTrafficMonitor.Services
{
    public static class SessionService
    {
        private static readonly string RecentFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HttpTrafficMonitor", "recent_sessions.json");

        public static void SaveSession(string path, SessionData session)
        {
            string json = JsonConvert.SerializeObject(session, Formatting.None,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            using var fileStream = File.Create(path);
            using var gzip = new GZipStream(fileStream, CompressionLevel.Optimal);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            gzip.Write(bytes, 0, bytes.Length);

            AddRecentSession(path);
        }

        public static SessionData LoadSession(string path)
        {
            using var fileStream = File.OpenRead(path);
            using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            string json = reader.ReadToEnd();

            var session = JsonConvert.DeserializeObject<SessionData>(json)
                ?? throw new InvalidDataException("Invalid session file.");

            AddRecentSession(path);
            return session;
        }

        public static SessionData BuildSessionData(
            IEnumerable<HttpRequestEntry> requests,
            string excludedDomains,
            string excludedProcesses,
            List<AlertRule>? alertRules = null,
            List<AutoResponderRule>? autoResponderRules = null,
            List<FilterPreset>? filterPresets = null,
            string sslPassthroughDomains = "")
        {
            return new SessionData
            {
                SavedAt = DateTime.Now,
                Requests = requests.Select(r => new SessionRequestEntry
                {
                    Id = r.Id,
                    Timestamp = r.Timestamp,
                    ProcessName = r.ProcessName,
                    ProcessId = r.ProcessId,
                    Method = r.Method,
                    Url = r.Url,
                    Host = r.Host,
                    Scheme = r.Scheme,
                    StatusCode = r.StatusCode,
                    ResponseSize = r.ResponseSize,
                    RequestHeaders = r.RequestHeaders,
                    RequestBody = r.RequestBody,
                    RequestContentType = r.RequestContentType,
                    ResponseHeaders = r.ResponseHeaders,
                    ResponseBody = r.ResponseBody,
                    ResponseContentType = r.ResponseContentType,
                    DurationMs = r.Duration?.TotalMilliseconds,
                    IsBookmarked = r.IsBookmarked,
                    BookmarkNotes = r.BookmarkNotes,
                }).ToList(),
                Settings = new SessionSettings
                {
                    ExcludedDomains = excludedDomains,
                    ExcludedProcesses = excludedProcesses,
                    SslPassthroughDomains = sslPassthroughDomains,
                    AlertRules = alertRules ?? new(),
                    AutoResponderRules = autoResponderRules ?? new(),
                    FilterPresets = filterPresets ?? new(),
                },
                Bookmarks = requests.Where(r => r.IsBookmarked)
                    .Select(r => new BookmarkData { RequestId = r.Id, Notes = r.BookmarkNotes ?? "" })
                    .ToList()
            };
        }

        public static List<HttpRequestEntry> RestoreRequests(SessionData session)
        {
            var bookmarkLookup = session.Bookmarks.ToDictionary(b => b.RequestId);
            return session.Requests.Select(r =>
            {
                var entry = new HttpRequestEntry
                {
                    Id = r.Id,
                    Timestamp = r.Timestamp,
                    ProcessName = r.ProcessName,
                    ProcessId = r.ProcessId,
                    Method = r.Method,
                    Url = r.Url,
                    Host = r.Host,
                    Scheme = r.Scheme,
                    StatusCode = r.StatusCode,
                    ResponseSize = r.ResponseSize,
                    RequestHeaders = r.RequestHeaders,
                    RequestBody = r.RequestBody,
                    RequestContentType = r.RequestContentType,
                    ResponseHeaders = r.ResponseHeaders,
                    ResponseBody = r.ResponseBody,
                    ResponseContentType = r.ResponseContentType,
                    IsComplete = r.StatusCode.HasValue,
                    IsBookmarked = r.IsBookmarked,
                    BookmarkNotes = r.BookmarkNotes,
                };
                if (r.DurationMs.HasValue)
                    entry.Duration = TimeSpan.FromMilliseconds(r.DurationMs.Value);
                if (bookmarkLookup.TryGetValue(r.Id, out var bm))
                {
                    entry.IsBookmarked = true;
                    entry.BookmarkNotes = bm.Notes;
                }
                return entry;
            }).ToList();
        }

        public static List<string> GetRecentSessions()
        {
            try
            {
                if (!File.Exists(RecentFilePath)) return new();
                string json = File.ReadAllText(RecentFilePath);
                var list = JsonConvert.DeserializeObject<List<string>>(json) ?? new();
                return list.Where(File.Exists).ToList();
            }
            catch { return new(); }
        }

        private static void AddRecentSession(string path)
        {
            try
            {
                var list = GetRecentSessions();
                list.Remove(path);
                list.Insert(0, path);
                if (list.Count > 10) list = list.Take(10).ToList();

                string dir = Path.GetDirectoryName(RecentFilePath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(RecentFilePath, JsonConvert.SerializeObject(list));
            }
            catch { }
        }
    }
}
