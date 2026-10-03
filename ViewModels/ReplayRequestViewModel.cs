using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using HttpTrafficMonitor.Helpers;
using HttpTrafficMonitor.Models;
using HttpTrafficMonitor.Services;

namespace HttpTrafficMonitor.ViewModels
{
    public class ReplayRequestViewModel : ViewModelBase, IDisposable
    {
        private readonly HttpReplayService _replayService = new();
        private string _method = "GET";
        private string _url = string.Empty;
        private string _requestHeaders = string.Empty;
        private string _requestBody = string.Empty;
        private string _responseHeaders = string.Empty;
        private string _responseBody = string.Empty;
        private string _originalResponseHeaders = string.Empty;
        private string _originalResponseBody = string.Empty;
        private int? _statusCode;
        private int? _originalStatusCode;
        private string _duration = string.Empty;
        private string _originalDuration = string.Empty;
        private bool _isSending;
        private bool _hasResponse;
        private bool _hasOriginal;
        private string _statusMessage = string.Empty;

        public static readonly string[] Methods = { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };

        public string Method { get => _method; set => SetProperty(ref _method, value); }
        public string Url { get => _url; set => SetProperty(ref _url, value); }
        public string RequestHeaders { get => _requestHeaders; set => SetProperty(ref _requestHeaders, value); }
        public string RequestBody { get => _requestBody; set => SetProperty(ref _requestBody, value); }
        public string ResponseHeaders { get => _responseHeaders; set => SetProperty(ref _responseHeaders, value); }
        public string ResponseBody { get => _responseBody; set => SetProperty(ref _responseBody, value); }
        public string OriginalResponseHeaders { get => _originalResponseHeaders; set => SetProperty(ref _originalResponseHeaders, value); }
        public string OriginalResponseBody { get => _originalResponseBody; set => SetProperty(ref _originalResponseBody, value); }
        public int? StatusCode { get => _statusCode; set => SetProperty(ref _statusCode, value); }
        public int? OriginalStatusCode { get => _originalStatusCode; set => SetProperty(ref _originalStatusCode, value); }
        public string Duration { get => _duration; set => SetProperty(ref _duration, value); }
        public string OriginalDuration { get => _originalDuration; set => SetProperty(ref _originalDuration, value); }
        public bool IsSending { get => _isSending; set { SetProperty(ref _isSending, value); OnPropertyChanged(nameof(IsNotSending)); } }
        public bool IsNotSending => !_isSending;
        public bool HasResponse { get => _hasResponse; set => SetProperty(ref _hasResponse, value); }
        public bool HasOriginal { get => _hasOriginal; set => SetProperty(ref _hasOriginal, value); }
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public ICommand SendCommand { get; }

        public ReplayRequestViewModel()
        {
            SendCommand = new RelayCommand(async () => await SendRequestAsync(), () => IsNotSending && !string.IsNullOrWhiteSpace(Url));
        }

        public void LoadFromRequest(HttpRequestEntry entry)
        {
            Method = entry.Method;
            Url = entry.Url;
            RequestHeaders = entry.RequestHeaders;
            RequestBody = entry.RequestBody;

            if (entry.IsComplete)
            {
                HasOriginal = true;
                OriginalStatusCode = entry.StatusCode;
                OriginalResponseHeaders = entry.ResponseHeaders ?? "";
                OriginalResponseBody = FormatHelper.FormatBody(entry.ResponseBody, entry.ResponseContentType);
                OriginalDuration = entry.DurationFormatted;
            }
        }

        private async Task SendRequestAsync()
        {
            IsSending = true;
            StatusMessage = "Sending request...";
            HasResponse = false;

            try
            {
                var headers = HttpReplayService.ParseHeaderList(RequestHeaders);
                var result = await _replayService.SendRequestAsync(Method, Url, headers,
                    string.IsNullOrWhiteSpace(RequestBody) ? null : RequestBody);

                if (result.IsSuccess)
                {
                    StatusCode = result.StatusCode;
                    ResponseHeaders = result.ResponseHeaders;
                    ResponseBody = FormatHelper.FormatBody(result.ResponseBody, null);
                    Duration = result.DurationFormatted;
                    HasResponse = true;
                    StatusMessage = $"Response: {result.StatusCode} ({result.DurationFormatted}, {FormatHelper.FormatSize(result.ResponseSize)})";
                }
                else
                {
                    StatusMessage = $"Error: {result.Error}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsSending = false;
            }
        }

        public void Dispose()
        {
            _replayService.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
