using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HttpTrafficMonitor.Models
{
    public class HttpRequestEntry : INotifyPropertyChanged
    {
        private int? _statusCode;
        private long? _responseSize;
        private string? _responseHeaders;
        private string? _responseBody;
        private string? _responseContentType;
        private TimeSpan? _duration;
        private DateTime? _responseTime;
        private bool _isComplete;
        private bool _isBookmarked;
        private string? _bookmarkNotes;
        private TlsCertificateInfo? _tlsInfo;
        private bool _isSlow;
        private double? _dnsLookupMs;
        private double? _tcpConnectMs;
        private double? _tlsHandshakeMs;
        private double? _timeToFirstByteMs;
        private double? _contentDownloadMs;

        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string ProcessName { get; set; } = "Unknown";
        public int ProcessId { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Scheme { get; set; } = "http";
        public string RequestHeaders { get; set; } = string.Empty;
        public string RequestBody { get; set; } = string.Empty;
        public string RequestContentType { get; set; } = string.Empty;

        public int? StatusCode
        {
            get => _statusCode;
            set { _statusCode = value; OnPropertyChanged(); }
        }

        public long? ResponseSize
        {
            get => _responseSize;
            set { _responseSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(ResponseSizeFormatted)); }
        }

        public string? ResponseHeaders
        {
            get => _responseHeaders;
            set { _responseHeaders = value; OnPropertyChanged(); }
        }

        public string? ResponseBody
        {
            get => _responseBody;
            set { _responseBody = value; OnPropertyChanged(); }
        }

        public string? ResponseContentType
        {
            get => _responseContentType;
            set { _responseContentType = value; OnPropertyChanged(); }
        }

        public TimeSpan? Duration
        {
            get => _duration;
            set { _duration = value; OnPropertyChanged(); OnPropertyChanged(nameof(DurationFormatted)); }
        }

        public DateTime? ResponseTime
        {
            get => _responseTime;
            set { _responseTime = value; OnPropertyChanged(); }
        }

        public bool IsComplete
        {
            get => _isComplete;
            set { _isComplete = value; OnPropertyChanged(); }
        }

        public string ResponseSizeFormatted
        {
            get
            {
                if (ResponseSize == null) return "-";
                double size = ResponseSize.Value;
                string[] units = { "B", "KB", "MB", "GB" };
                int unitIndex = 0;
                while (size >= 1024 && unitIndex < units.Length - 1)
                {
                    size /= 1024;
                    unitIndex++;
                }
                return $"{size:F1} {units[unitIndex]}";
            }
        }

        public string DurationFormatted
        {
            get
            {
                if (Duration == null) return "-";
                if (Duration.Value.TotalMilliseconds < 1000)
                    return $"{Duration.Value.TotalMilliseconds:F0} ms";
                return $"{Duration.Value.TotalSeconds:F2} s";
            }
        }

        public string TimestampFormatted => Timestamp.ToString("HH:mm:ss.fff");

        // Bookmark support
        public bool IsBookmarked
        {
            get => _isBookmarked;
            set { _isBookmarked = value; OnPropertyChanged(); OnPropertyChanged(nameof(BookmarkIcon)); }
        }
        public string? BookmarkNotes
        {
            get => _bookmarkNotes;
            set { _bookmarkNotes = value; OnPropertyChanged(); }
        }
        public string BookmarkIcon => _isBookmarked ? "\u2605" : "\u2606";

        // TLS/SSL certificate info
        public TlsCertificateInfo? TlsInfo
        {
            get => _tlsInfo;
            set { _tlsInfo = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTlsInfo)); }
        }
        public bool HasTlsInfo => _tlsInfo != null;

        // WebSocket support
        public bool IsWebSocket { get; set; }
        public List<WebSocketMessage> WebSocketMessages { get; set; } = new();

        // Performance timing breakdown (null = not measured for this request)
        public double? DnsLookupMs
        {
            get => _dnsLookupMs;
            set { _dnsLookupMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimingTotalMs)); }
        }
        public double? TcpConnectMs
        {
            get => _tcpConnectMs;
            set { _tcpConnectMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimingTotalMs)); }
        }
        public double? TlsHandshakeMs
        {
            get => _tlsHandshakeMs;
            set { _tlsHandshakeMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimingTotalMs)); }
        }
        public double? TimeToFirstByteMs
        {
            get => _timeToFirstByteMs;
            set { _timeToFirstByteMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimingTotalMs)); }
        }
        public double? ContentDownloadMs
        {
            get => _contentDownloadMs;
            set { _contentDownloadMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimingTotalMs)); }
        }
        public double TimingTotalMs =>
            (_dnsLookupMs ?? 0) + (_tcpConnectMs ?? 0) + (_tlsHandshakeMs ?? 0) + (_timeToFirstByteMs ?? 0) + (_contentDownloadMs ?? 0);

        // SSL Passthrough indicator (CONNECT logged without MITM)
        public bool IsSslPassthrough { get; set; }

        // Slow request indicator
        public bool IsSlow
        {
            get => _isSlow;
            set { _isSlow = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
