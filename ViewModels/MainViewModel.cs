using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using HttpTrafficMonitor.Helpers;
using HttpTrafficMonitor.Models;
using HttpTrafficMonitor.Services;
using HttpTrafficMonitor.Views;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace HttpTrafficMonitor.ViewModels
{
    public class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly ProxyService _proxyService;
        private readonly AlertService _alertService;
        private readonly ThemeService _themeService;
        private readonly object _collectionLock = new();
        private HttpRequestEntry? _selectedRequest;
        private string _filterText = string.Empty;
        private string _selectedProcessFilter = "All Processes";
        private string _selectedDomainFilter = "All Domains";
        private string _selectedMethodFilter = "All Methods";
        private string _selectedStatusFilter = "All Status Codes";
        private bool _isMonitoring;
        private bool _isPaused;
        private bool _autoScroll = true;
        private long _totalDataTransferred;
        private int _totalRequests;
        private int _getCount;
        private int _postCount;
        private int _putCount;
        private int _deleteCount;
        private int _errorCount;
        private string _statusMessage = "Ready. Click Start to begin monitoring.";
        private string _excludedDomainsText = string.Empty;
        private string _excludedProcessesText = string.Empty;
        private string _sslPassthroughDomainsText = string.Empty;
        private string _formattedRequestBody = string.Empty;
        private string _formattedResponseBody = string.Empty;
        private bool _showBottomPanel = true;
        private bool _showBookmarksOnly;
        private bool _isDarkTheme = true;
        private string _decodedRequestContent = string.Empty;
        private string _decodedResponseContent = string.Empty;
        private string _decodedRequestInfo = string.Empty;
        private string _decodedResponseInfo = string.Empty;

        // Advanced filter
        private readonly AdvancedFilterViewModel _advancedFilterVm = new();
        private bool _isAdvancedFilterActive;

        public const int MaxRequests = 50_000;

        public ObservableCollection<HttpRequestEntry> AllRequests { get; } = new();
        public ICollectionView FilteredRequests { get; }
        public ObservableCollection<string> ProcessFilterOptions { get; } = new() { "All Processes" };
        public ObservableCollection<string> DomainFilterOptions { get; } = new() { "All Domains" };
        public ObservableCollection<string> MethodFilterOptions { get; } = new()
            { "All Methods", "GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD" };
        public ObservableCollection<string> StatusFilterOptions { get; } = new()
            { "All Status Codes", "2xx Success", "3xx Redirect", "4xx Client Error", "5xx Server Error" };
        public ObservableCollection<string> RecentSessions { get; } = new();

        // Multi-select support
        public ObservableCollection<HttpRequestEntry> SelectedRequests { get; } = new();

        // IPC API access
        public ProxyService ProxyServiceInstance => _proxyService;
        public AlertService AlertServiceInstance => _alertService;
        public object CollectionLock => _collectionLock;
        public AdvancedFilterViewModel AdvancedFilterVm => _advancedFilterVm;

        // Sub-ViewModels
        public GraphsViewModel GraphsVm { get; }
        public AlertsViewModel AlertsVm { get; }
        public AutoResponderViewModel AutoResponderVm { get; }

        public HttpRequestEntry? SelectedRequest
        {
            get => _selectedRequest;
            set
            {
                if (SetProperty(ref _selectedRequest, value))
                {
                    UpdateFormattedBodies();
                    UpdateDecodedContent();
                    OnPropertyChanged(nameof(HasSelectedRequest));
                }
            }
        }
        public bool HasSelectedRequest => _selectedRequest != null;

        public string FilterText
        {
            get => _filterText;
            set { if (SetProperty(ref _filterText, value)) FilteredRequests.Refresh(); }
        }
        public string SelectedProcessFilter
        {
            get => _selectedProcessFilter;
            set { if (SetProperty(ref _selectedProcessFilter, value)) FilteredRequests.Refresh(); }
        }
        public string SelectedDomainFilter
        {
            get => _selectedDomainFilter;
            set { if (SetProperty(ref _selectedDomainFilter, value)) FilteredRequests.Refresh(); }
        }
        public string SelectedMethodFilter
        {
            get => _selectedMethodFilter;
            set { if (SetProperty(ref _selectedMethodFilter, value)) FilteredRequests.Refresh(); }
        }
        public string SelectedStatusFilter
        {
            get => _selectedStatusFilter;
            set { if (SetProperty(ref _selectedStatusFilter, value)) FilteredRequests.Refresh(); }
        }
        public bool IsMonitoring
        {
            get => _isMonitoring;
            set { if (SetProperty(ref _isMonitoring, value)) { OnPropertyChanged(nameof(IsNotMonitoring)); CommandManager.InvalidateRequerySuggested(); } }
        }
        public bool IsNotMonitoring => !_isMonitoring;
        public bool IsPaused
        {
            get => _isPaused;
            set { if (SetProperty(ref _isPaused, value)) { _proxyService.IsPaused = value; StatusMessage = value ? "Capture paused." : "Monitoring traffic..."; } }
        }
        public bool AutoScroll { get => _autoScroll; set => SetProperty(ref _autoScroll, value); }
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
        public int TotalRequests { get => _totalRequests; set => SetProperty(ref _totalRequests, value); }
        public int GetCount { get => _getCount; set => SetProperty(ref _getCount, value); }
        public int PostCount { get => _postCount; set => SetProperty(ref _postCount, value); }
        public int PutCount { get => _putCount; set => SetProperty(ref _putCount, value); }
        public int DeleteCount { get => _deleteCount; set => SetProperty(ref _deleteCount, value); }
        public int ErrorCount { get => _errorCount; set => SetProperty(ref _errorCount, value); }
        public long TotalDataTransferred
        {
            get => _totalDataTransferred;
            set { SetProperty(ref _totalDataTransferred, value); OnPropertyChanged(nameof(TotalDataFormatted)); }
        }
        public string TotalDataFormatted => FormatHelper.FormatSize(_totalDataTransferred);
        public string ExcludedDomainsText { get => _excludedDomainsText; set { if (SetProperty(ref _excludedDomainsText, value)) UpdateExcludedDomains(); } }
        public string ExcludedProcessesText { get => _excludedProcessesText; set { if (SetProperty(ref _excludedProcessesText, value)) UpdateExcludedProcesses(); } }
        public string SslPassthroughDomainsText { get => _sslPassthroughDomainsText; set { if (SetProperty(ref _sslPassthroughDomainsText, value)) UpdateSslPassthroughDomains(); } }
        public string FormattedRequestBody { get => _formattedRequestBody; set => SetProperty(ref _formattedRequestBody, value); }
        public string FormattedResponseBody { get => _formattedResponseBody; set => SetProperty(ref _formattedResponseBody, value); }
        public bool ShowBottomPanel { get => _showBottomPanel; set => SetProperty(ref _showBottomPanel, value); }
        public bool ShowBookmarksOnly
        {
            get => _showBookmarksOnly;
            set { if (SetProperty(ref _showBookmarksOnly, value)) FilteredRequests.Refresh(); }
        }
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set { if (SetProperty(ref _isDarkTheme, value)) { _themeService.SetTheme(value); } }
        }
        public bool IsAdvancedFilterActive { get => _isAdvancedFilterActive; set { if (SetProperty(ref _isAdvancedFilterActive, value)) FilteredRequests.Refresh(); } }

        // Decoded content
        public string DecodedRequestContent { get => _decodedRequestContent; set => SetProperty(ref _decodedRequestContent, value); }
        public string DecodedResponseContent { get => _decodedResponseContent; set => SetProperty(ref _decodedResponseContent, value); }
        public string DecodedRequestInfo { get => _decodedRequestInfo; set => SetProperty(ref _decodedRequestInfo, value); }
        public string DecodedResponseInfo { get => _decodedResponseInfo; set => SetProperty(ref _decodedResponseInfo, value); }

        // Commands
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ExportCurlCommand { get; }
        public ICommand ExportPostmanCommand { get; }
        public ICommand ExportHarCommand { get; }
        public ICommand ExportSelectedCommand { get; }
        public ICommand CopyUrlCommand { get; }
        public ICommand CopyRequestHeadersCommand { get; }
        public ICommand CopyResponseHeadersCommand { get; }
        public ICommand CopyRequestBodyCommand { get; }
        public ICommand CopyResponseBodyCommand { get; }
        public ICommand CopyCurlCommand { get; }
        public ICommand SaveRequestCommand { get; }
        public ICommand SaveResponseCommand { get; }
        public ICommand ExcludeDomainCommand { get; }
        public ICommand ExcludeProcessCommand { get; }
        public ICommand ReplayRequestCommand { get; }
        public ICommand ComposeRequestCommand { get; }
        public ICommand CompareSelectedCommand { get; }
        public ICommand ToggleBookmarkCommand { get; }
        public ICommand SaveSessionCommand { get; }
        public ICommand LoadSessionCommand { get; }
        public ICommand LoadRecentSessionCommand { get; }
        public ICommand AdvancedFilterCommand { get; }
        public ICommand ToggleBottomPanelCommand { get; }

        public event Action? ScrollToBottomRequested;

        public MainViewModel()
        {
            _proxyService = new ProxyService();
            _alertService = new AlertService();
            _themeService = new ThemeService();

            _proxyService.RequestCaptured += OnRequestCaptured;
            _proxyService.ResponseUpdated += OnResponseUpdated;
            _proxyService.ErrorOccurred += OnProxyError;

            GraphsVm = new GraphsViewModel();
            AlertsVm = new AlertsViewModel(_alertService);
            AutoResponderVm = new AutoResponderViewModel(_proxyService.AutoResponderRules);

            BindingOperations.EnableCollectionSynchronization(AllRequests, _collectionLock);
            FilteredRequests = CollectionViewSource.GetDefaultView(AllRequests);
            FilteredRequests.Filter = ApplyFilter;

            // Core commands
            StartCommand = new RelayCommand(ExecuteStart, () => !IsMonitoring);
            StopCommand = new RelayCommand(ExecuteStop, () => IsMonitoring);
            ClearCommand = new RelayCommand(ExecuteClear);
            ExportCommand = new RelayCommand(ExecuteExport, () => AllRequests.Count > 0);

            // Context menu
            CopyUrlCommand = new RelayCommand(_ => CopyToClipboard(SelectedRequest?.Url), _ => HasSelectedRequest);
            CopyRequestHeadersCommand = new RelayCommand(_ => CopyToClipboard(SelectedRequest?.RequestHeaders), _ => HasSelectedRequest);
            CopyResponseHeadersCommand = new RelayCommand(_ => CopyToClipboard(SelectedRequest?.ResponseHeaders), _ => HasSelectedRequest);
            CopyRequestBodyCommand = new RelayCommand(_ => CopyToClipboard(SelectedRequest?.RequestBody), _ => HasSelectedRequest);
            CopyResponseBodyCommand = new RelayCommand(_ => CopyToClipboard(SelectedRequest?.ResponseBody), _ => HasSelectedRequest);
            CopyCurlCommand = new RelayCommand(_ => { if (SelectedRequest != null) CopyToClipboard(ExportService.ToCurl(SelectedRequest)); }, _ => HasSelectedRequest);
            SaveRequestCommand = new RelayCommand(_ => SaveToFile("request", SelectedRequest), _ => HasSelectedRequest);
            SaveResponseCommand = new RelayCommand(_ => SaveToFile("response", SelectedRequest), _ => HasSelectedRequest);
            ExcludeDomainCommand = new RelayCommand(_ => AddExcludedDomain(), _ => HasSelectedRequest);
            ExcludeProcessCommand = new RelayCommand(_ => AddExcludedProcess(), _ => HasSelectedRequest);

            // Export variants
            ExportCurlCommand = new RelayCommand(_ => { if (SelectedRequest != null) CopyToClipboard(ExportService.ToCurl(SelectedRequest)); }, _ => HasSelectedRequest);
            ExportPostmanCommand = new RelayCommand(_ => ExportPostman(), _ => AllRequests.Count > 0);
            ExportHarCommand = new RelayCommand(_ => ExportHar(), _ => AllRequests.Count > 0);
            ExportSelectedCommand = new RelayCommand(_ => ExportSelected(), _ => SelectedRequests.Count > 0);

            // New feature commands
            ReplayRequestCommand = new RelayCommand(_ => OpenReplayWindow(), _ => HasSelectedRequest);
            ComposeRequestCommand = new RelayCommand(_ => OpenReplayWindow(compose: true));
            CompareSelectedCommand = new RelayCommand(_ => OpenComparisonWindow(), _ => SelectedRequests.Count >= 2);
            ToggleBookmarkCommand = new RelayCommand(entry => ToggleBookmark(entry as HttpRequestEntry ?? SelectedRequest), entry => entry is HttpRequestEntry || HasSelectedRequest);
            SaveSessionCommand = new RelayCommand(_ => ExecuteSaveSession(), _ => AllRequests.Count > 0);
            LoadSessionCommand = new RelayCommand(_ => ExecuteLoadSession());
            LoadRecentSessionCommand = new RelayCommand(path => { if (path is string p) LoadSessionFromFile(p); });
            AdvancedFilterCommand = new RelayCommand(_ => OpenAdvancedFilter());
            ToggleBottomPanelCommand = new RelayCommand(() => ShowBottomPanel = !ShowBottomPanel);

            // Load recent sessions
            foreach (var path in SessionService.GetRecentSessions())
                RecentSessions.Add(path);

            _themeService.Initialize();
            _isDarkTheme = _themeService.IsDarkTheme;
        }

        private void ExecuteStart()
        {
            if (_proxyService.IsAwaitingCertificateConfirmation)
            {
                StatusMessage = ProxyService.CertificateConfirmationMessage;
                return;
            }

            try
            {
                _proxyService.AutoResponderEnabled = AutoResponderVm.IsEnabled;
                _proxyService.Start();
                IsMonitoring = true;
                IsPaused = false;
                StatusMessage = _proxyService.MonitoringStatusMessage;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start monitoring:\n\n{ex.Message}\n\nMake sure no other proxy is running on port {ProxyService.ProxyPort} and the app is running as Administrator.",
                    "Start Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = $"Failed to start monitoring: {ex.Message}";
            }
        }

        public void ExecuteStop()
        {
            try
            {
                _proxyService.Stop();
                IsMonitoring = false;
                IsPaused = false;
                StatusMessage = "Monitoring stopped. System proxy restored.";
            }
            catch (Exception ex) { StatusMessage = $"Error stopping: {ex.Message}"; }
        }

        private void ExecuteClear()
        {
            lock (_collectionLock) { AllRequests.Clear(); }
            ProcessFilterOptions.Clear(); ProcessFilterOptions.Add("All Processes");
            DomainFilterOptions.Clear(); DomainFilterOptions.Add("All Domains");
            TotalRequests = 0; GetCount = 0; PostCount = 0; PutCount = 0; DeleteCount = 0; ErrorCount = 0;
            TotalDataTransferred = 0; SelectedRequest = null;
            GraphsVm.Reset();
            StatusMessage = IsMonitoring ? "Logs cleared. Still monitoring..." : "Logs cleared.";
        }

        private void ExecuteExport()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|CSV Files (*.csv)|*.csv|HAR Files (*.har)|*.har|Postman Collection (*.json)|*.json",
                DefaultExt = ".json",
                FileName = $"traffic_export_{DateTime.Now:yyyyMMdd_HHmmss}"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                List<HttpRequestEntry> snapshot;
                lock (_collectionLock) { snapshot = AllRequests.ToList(); }

                string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                if (ext == ".csv") ExportToCsv(dialog.FileName, snapshot);
                else if (ext == ".har") File.WriteAllText(dialog.FileName, ExportService.ToHar(snapshot));
                else ExportToJson(dialog.FileName, snapshot);

                StatusMessage = $"Exported {snapshot.Count} requests to {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportPostman()
        {
            var dialog = new SaveFileDialog { Filter = "JSON Files (*.json)|*.json", FileName = "postman_collection.json" };
            if (dialog.ShowDialog() != true) return;
            List<HttpRequestEntry> snapshot;
            lock (_collectionLock) { snapshot = AllRequests.ToList(); }
            File.WriteAllText(dialog.FileName, ExportService.ToPostmanCollection(snapshot));
            StatusMessage = $"Exported Postman collection ({snapshot.Count} requests)";
        }

        private void ExportHar()
        {
            var dialog = new SaveFileDialog { Filter = "HAR Files (*.har)|*.har", FileName = $"capture_{DateTime.Now:yyyyMMdd_HHmmss}.har" };
            if (dialog.ShowDialog() != true) return;
            List<HttpRequestEntry> snapshot;
            lock (_collectionLock) { snapshot = AllRequests.ToList(); }
            File.WriteAllText(dialog.FileName, ExportService.ToHar(snapshot));
            StatusMessage = $"Exported HAR ({snapshot.Count} entries)";
        }

        private void ExportSelected()
        {
            var dialog = new SaveFileDialog { Filter = "JSON Files (*.json)|*.json|HAR Files (*.har)|*.har", FileName = "selected_export.json" };
            if (dialog.ShowDialog() != true) return;
            var selected = SelectedRequests.ToList();
            string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            if (ext == ".har") File.WriteAllText(dialog.FileName, ExportService.ToHar(selected));
            else ExportToJson(dialog.FileName, selected);
            StatusMessage = $"Exported {selected.Count} selected requests";
        }

        // Session save/load
        private void ExecuteSaveSession()
        {
            var dialog = new SaveFileDialog { Filter = "HTS Session (*.hts)|*.hts", FileName = $"session_{DateTime.Now:yyyyMMdd_HHmmss}.hts" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                List<HttpRequestEntry> snapshot;
                lock (_collectionLock) { snapshot = AllRequests.ToList(); }
                var session = SessionService.BuildSessionData(snapshot, ExcludedDomainsText, ExcludedProcessesText,
                    AlertsVm.Rules.ToList(), AutoResponderVm.Rules.ToList(),
                    sslPassthroughDomains: SslPassthroughDomainsText);
                SessionService.SaveSession(dialog.FileName, session);
                RefreshRecentSessions();
                StatusMessage = $"Session saved ({snapshot.Count} requests)";
            }
            catch (Exception ex) { MessageBox.Show($"Save failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void ExecuteLoadSession()
        {
            var dialog = new OpenFileDialog { Filter = "HTS Session (*.hts)|*.hts" };
            if (dialog.ShowDialog() != true) return;
            LoadSessionFromFile(dialog.FileName);
        }

        private void LoadSessionFromFile(string path)
        {
            try
            {
                var session = SessionService.LoadSession(path);
                var entries = SessionService.RestoreRequests(session);

                ExecuteClear();
                lock (_collectionLock)
                {
                    foreach (var e in entries) AllRequests.Add(e);
                }
                TotalRequests = entries.Count;
                foreach (var e in entries)
                {
                    UpdateMethodCount(e.Method);
                    if (e.ResponseSize.HasValue) TotalDataTransferred += e.ResponseSize.Value;
                    if (e.StatusCode >= 400) ErrorCount++;
                    if (!ProcessFilterOptions.Contains(e.ProcessName)) ProcessFilterOptions.Add(e.ProcessName);
                    if (!string.IsNullOrEmpty(e.Host) && !DomainFilterOptions.Contains(e.Host)) DomainFilterOptions.Add(e.Host);
                }

                ExcludedDomainsText = session.Settings.ExcludedDomains;
                ExcludedProcessesText = session.Settings.ExcludedProcesses;
                SslPassthroughDomainsText = session.Settings.SslPassthroughDomains ?? string.Empty;
                RefreshRecentSessions();
                StatusMessage = $"Loaded session: {entries.Count} requests from {session.SavedAt:g}";
            }
            catch (Exception ex) { MessageBox.Show($"Load failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void RefreshRecentSessions()
        {
            RecentSessions.Clear();
            foreach (var p in SessionService.GetRecentSessions())
                RecentSessions.Add(p);
        }

        // Replay / Compose
        private void OpenReplayWindow(bool compose = false)
        {
            var window = new ReplayRequestWindow(compose ? null : SelectedRequest);
            window.Owner = Application.Current.MainWindow;
            window.Show();
        }

        // Comparison
        private void OpenComparisonWindow()
        {
            if (SelectedRequests.Count < 2) return;
            var window = new ComparisonWindow(SelectedRequests[0], SelectedRequests[1]);
            window.Owner = Application.Current.MainWindow;
            window.Show();
        }

        // Bookmarks: the star of a row toggles that row, which is not selected yet when it is clicked
        private void ToggleBookmark(HttpRequestEntry? entry)
        {
            if (entry == null) return;
            entry.IsBookmarked = !entry.IsBookmarked;
        }

        // Advanced filter
        private void OpenAdvancedFilter()
        {
            var dialog = new AdvancedFilterDialog(_advancedFilterVm);
            _advancedFilterVm.Applied += () =>
            {
                IsAdvancedFilterActive = _advancedFilterVm.Conditions.Count > 0;
                FilteredRequests.Refresh();
            };
            dialog.Owner = Application.Current.MainWindow;
            dialog.Show();
        }

        // Decoded content
        private void UpdateDecodedContent()
        {
            if (SelectedRequest == null)
            {
                DecodedRequestContent = DecodedResponseContent = DecodedRequestInfo = DecodedResponseInfo = string.Empty;
                return;
            }

            if (!string.IsNullOrEmpty(SelectedRequest.RequestBody))
            {
                var decoded = ContentDecoder.Decode(SelectedRequest.RequestBody);
                DecodedRequestContent = decoded.Decoded;
                DecodedRequestInfo = decoded.DecodingApplied;
            }
            else { DecodedRequestContent = string.Empty; DecodedRequestInfo = "No request body"; }

            if (!string.IsNullOrEmpty(SelectedRequest.ResponseBody))
            {
                var decoded = ContentDecoder.Decode(SelectedRequest.ResponseBody, SelectedRequest.ResponseContentType);
                DecodedResponseContent = decoded.Decoded;
                DecodedResponseInfo = decoded.DecodingApplied;
            }
            else { DecodedResponseContent = string.Empty; DecodedResponseInfo = "No response body"; }
        }

        // Event handlers
        private void OnRequestCaptured(HttpRequestEntry entry)
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                lock (_collectionLock)
                {
                    while (AllRequests.Count >= MaxRequests) AllRequests.RemoveAt(0);
                    AllRequests.Add(entry);
                }
                TotalRequests++;
                UpdateMethodCount(entry.Method);
                if (!ProcessFilterOptions.Contains(entry.ProcessName)) ProcessFilterOptions.Add(entry.ProcessName);
                if (!string.IsNullOrEmpty(entry.Host) && !DomainFilterOptions.Contains(entry.Host)) DomainFilterOptions.Add(entry.Host);

                GraphsVm.RecordRequest(entry);
                if (AutoScroll) ScrollToBottomRequested?.Invoke();
            });
        }

        private void OnResponseUpdated(HttpRequestEntry entry)
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                if (entry.ResponseSize.HasValue) TotalDataTransferred += entry.ResponseSize.Value;
                if (entry.StatusCode.HasValue && entry.StatusCode >= 400) ErrorCount++;
                if (SelectedRequest == entry) { UpdateFormattedBodies(); UpdateDecodedContent(); }

                GraphsVm.RecordResponse(entry);
                _alertService.Evaluate(entry);
            });
        }

        private void OnProxyError(string message)
        {
            Application.Current?.Dispatcher?.InvokeAsync(() => StatusMessage = message);
        }

        private void UpdateMethodCount(string method)
        {
            switch (method.ToUpperInvariant())
            {
                case "GET": GetCount++; break;
                case "POST": PostCount++; break;
                case "PUT": PutCount++; break;
                case "DELETE": DeleteCount++; break;
            }
        }

        private void UpdateFormattedBodies()
        {
            if (SelectedRequest == null) { FormattedRequestBody = FormattedResponseBody = string.Empty; return; }
            FormattedRequestBody = !string.IsNullOrEmpty(SelectedRequest.RequestBody)
                ? FormatHelper.FormatBody(SelectedRequest.RequestBody, SelectedRequest.RequestContentType) : string.Empty;
            FormattedResponseBody = !string.IsNullOrEmpty(SelectedRequest.ResponseBody)
                ? FormatHelper.FormatBody(SelectedRequest.ResponseBody, SelectedRequest.ResponseContentType) : string.Empty;
        }

        private bool ApplyFilter(object obj)
        {
            if (obj is not HttpRequestEntry entry) return false;
            if (_showBookmarksOnly && !entry.IsBookmarked) return false;
            if (_selectedProcessFilter != "All Processes" && entry.ProcessName != _selectedProcessFilter) return false;
            if (_selectedDomainFilter != "All Domains" && !string.Equals(entry.Host, _selectedDomainFilter, StringComparison.OrdinalIgnoreCase)) return false;
            if (_selectedMethodFilter != "All Methods" && entry.Method != _selectedMethodFilter) return false;

            if (_selectedStatusFilter != "All Status Codes")
            {
                int? code = entry.StatusCode;
                bool matches = _selectedStatusFilter switch
                {
                    "2xx Success" => code >= 200 && code < 300,
                    "3xx Redirect" => code >= 300 && code < 400,
                    "4xx Client Error" => code >= 400 && code < 500,
                    "5xx Server Error" => code >= 500,
                    _ => true
                };
                if (!matches) return false;
            }

            if (!string.IsNullOrWhiteSpace(_filterText))
            {
                string f = _filterText.Trim();
                if (!entry.Url.Contains(f, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Host.Contains(f, StringComparison.OrdinalIgnoreCase) &&
                    !entry.ProcessName.Contains(f, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Method.Contains(f, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (_isAdvancedFilterActive && !_advancedFilterVm.EvaluateEntry(entry))
                return false;

            return true;
        }

        private void UpdateExcludedDomains()
        {
            _proxyService.ExcludedDomains.Clear();
            if (string.IsNullOrWhiteSpace(_excludedDomainsText)) return;
            foreach (string d in _excludedDomainsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                _proxyService.ExcludedDomains.Add(d);
        }

        private void UpdateExcludedProcesses()
        {
            _proxyService.ExcludedProcesses.Clear();
            if (string.IsNullOrWhiteSpace(_excludedProcessesText)) return;
            foreach (string p in _excludedProcessesText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                _proxyService.ExcludedProcesses.Add(p);
        }

        private void UpdateSslPassthroughDomains()
        {
            _proxyService.SslPassthroughDomains.Clear();
            if (string.IsNullOrWhiteSpace(_sslPassthroughDomainsText)) return;
            foreach (string d in _sslPassthroughDomainsText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                _proxyService.SslPassthroughDomains.Add(d);
        }

        private void AddExcludedDomain()
        {
            if (SelectedRequest == null || string.IsNullOrEmpty(SelectedRequest.Host)) return;
            if (!string.IsNullOrWhiteSpace(ExcludedDomainsText)) ExcludedDomainsText += "\n";
            ExcludedDomainsText += SelectedRequest.Host;
        }

        private void AddExcludedProcess()
        {
            if (SelectedRequest == null || string.IsNullOrEmpty(SelectedRequest.ProcessName) || SelectedRequest.ProcessName == "Unknown") return;
            if (!string.IsNullOrWhiteSpace(ExcludedProcessesText)) ExcludedProcessesText += "\n";
            ExcludedProcessesText += SelectedRequest.ProcessName;
        }

        private static void CopyToClipboard(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); } catch { }
        }

        private static void SaveToFile(string type, HttpRequestEntry? entry)
        {
            if (entry == null) return;
            var dialog = new SaveFileDialog { Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*", FileName = $"{type}_{entry.Id}_{entry.Host}_{DateTime.Now:HHmmss}.txt" };
            if (dialog.ShowDialog() != true) return;
            var sb = new StringBuilder();
            if (type == "request") { sb.AppendLine("=== Request Headers ==="); sb.AppendLine(entry.RequestHeaders); sb.AppendLine(); sb.AppendLine("=== Request Body ==="); sb.AppendLine(entry.RequestBody); }
            else { sb.AppendLine("=== Response Headers ==="); sb.AppendLine(entry.ResponseHeaders); sb.AppendLine(); sb.AppendLine("=== Response Body ==="); sb.AppendLine(entry.ResponseBody); }
            File.WriteAllText(dialog.FileName, sb.ToString());
        }

        private static void ExportToJson(string path, List<HttpRequestEntry> entries)
        {
            var data = entries.Select(e => new { e.Id, Timestamp = e.Timestamp.ToString("O"), e.ProcessName, e.ProcessId, e.Method, e.Url, e.Host, e.StatusCode, e.ResponseSize, Duration = e.Duration?.TotalMilliseconds, e.RequestHeaders, e.RequestBody, e.ResponseHeaders, e.ResponseBody });
            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
        }

        private static void ExportToCsv(string path, List<HttpRequestEntry> entries)
        {
            File.WriteAllText(path, ExportService.ToCsv(entries));
        }

        public void Dispose()
        {
            ExecuteStop();
            _proxyService.Dispose();
            GraphsVm.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
