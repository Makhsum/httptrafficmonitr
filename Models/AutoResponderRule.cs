using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace HttpTrafficMonitor.Models
{
    public class AutoResponderRule : INotifyPropertyChanged
    {
        private bool _isEnabled = true;
        private int _matchCount;

        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        public string UrlPattern { get; set; } = string.Empty;
        public bool IsRegex { get; set; }
        public string HttpMethod { get; set; } = "ANY";
        public int ResponseStatusCode { get; set; } = 200;
        public string ResponseHeaders { get; set; } = "Content-Type: application/json";
        public string ResponseBody { get; set; } = string.Empty;
        public string? ResponseFilePath { get; set; }
        public int DelayMs { get; set; }

        public int MatchCount
        {
            get => _matchCount;
            set { _matchCount = value; OnPropertyChanged(); }
        }

        public bool Matches(string url, string method)
        {
            if (!IsEnabled) return false;
            if (HttpMethod != "ANY" && !string.Equals(HttpMethod, method, StringComparison.OrdinalIgnoreCase))
                return false;

            if (IsRegex)
            {
                try { return Regex.IsMatch(url, UrlPattern, RegexOptions.IgnoreCase); }
                catch { return false; }
            }

            return url.Contains(UrlPattern, StringComparison.OrdinalIgnoreCase);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
