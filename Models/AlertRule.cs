using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HttpTrafficMonitor.Models
{
    public enum AlertRuleType
    {
        StatusCode,
        ResponseTime,
        Domain,
        Process,
        RequestSize,
        ResponseSize
    }

    public class AlertRule : INotifyPropertyChanged
    {
        private bool _isEnabled = true;
        private string _name = string.Empty;

        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }
        public AlertRuleType Type { get; set; }
        public string Pattern { get; set; } = string.Empty;
        public int? StatusCodeMin { get; set; }
        public int? StatusCodeMax { get; set; }
        public int? ResponseTimeThresholdMs { get; set; }
        public long? SizeThresholdBytes { get; set; }
        public bool PlaySound { get; set; }
        public bool ShowToast { get; set; } = true;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class AlertEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public AlertRule Rule { get; set; } = null!;
        public HttpRequestEntry Request { get; set; } = null!;
        public string Message { get; set; } = string.Empty;
        public string TimestampFormatted => Timestamp.ToString("HH:mm:ss.fff");
    }
}
