using System;
using System.Collections.Generic;

namespace HttpTrafficMonitor.Models
{
    public class SessionData
    {
        public string Version { get; set; } = "1.0";
        public DateTime SavedAt { get; set; } = DateTime.Now;
        public string Description { get; set; } = string.Empty;
        public List<SessionRequestEntry> Requests { get; set; } = new();
        public SessionSettings Settings { get; set; } = new();
        public List<BookmarkData> Bookmarks { get; set; } = new();
    }

    public class SessionRequestEntry
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public DateTime? ResponseTime { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Scheme { get; set; } = string.Empty;
        public int? StatusCode { get; set; }
        public long? ResponseSize { get; set; }
        public string RequestHeaders { get; set; } = string.Empty;
        public string RequestBody { get; set; } = string.Empty;
        public string RequestContentType { get; set; } = string.Empty;
        public string? ResponseHeaders { get; set; }
        public string? ResponseBody { get; set; }
        public string? ResponseContentType { get; set; }
        public double? DurationMs { get; set; }
        public double? DnsLookupMs { get; set; }
        public double? TcpConnectMs { get; set; }
        public double? TlsHandshakeMs { get; set; }
        public double? TimeToFirstByteMs { get; set; }
        public double? ContentDownloadMs { get; set; }
        public bool IsBookmarked { get; set; }
        public string? BookmarkNotes { get; set; }
    }

    public class SessionSettings
    {
        public string ExcludedDomains { get; set; } = string.Empty;
        public string ExcludedProcesses { get; set; } = string.Empty;
        public string SslPassthroughDomains { get; set; } = string.Empty;
        public List<FilterPreset> FilterPresets { get; set; } = new();
        public List<AlertRule> AlertRules { get; set; } = new();
        public List<AutoResponderRule> AutoResponderRules { get; set; } = new();
    }

    public class BookmarkData
    {
        public int RequestId { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}
