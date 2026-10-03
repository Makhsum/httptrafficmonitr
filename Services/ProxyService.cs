using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HttpTrafficMonitor.Helpers;
using HttpTrafficMonitor.Models;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace HttpTrafficMonitor.Services
{
    public class ProxyService : IDisposable
    {
        private ProxyServer? _proxyServer;
        private ExplicitProxyEndPoint? _explicitEndPoint;
        private int _requestCounter;
        private bool _isRunning;
        private bool _isPaused;
        private volatile bool _isAwaitingCertificateConfirmation;
        private readonly object _lock = new();
        private readonly Dictionary<int, string> _processNameCache = new();
        private DateTime _lastCacheClear = DateTime.UtcNow;

        // TLS certificate cache per host
        private readonly ConcurrentDictionary<string, TlsCertificateInfo> _tlsCertCache = new();

        public const int ProxyPort = 18080;
        public const int SlowRequestThresholdMs = 3000;
        public const string CertificateConfirmationMessage =
            "A confirmation is waiting on the desktop: Windows asks whether to trust the HttpTrafficMonitor root certificate. " +
            "The proxy starts once it is answered.";

        public event Action<HttpRequestEntry>? RequestCaptured;
        public event Action<HttpRequestEntry>? ResponseUpdated;
        public event Action<string>? ErrorOccurred;

        public bool IsRunning => _isRunning;
        public bool IsAwaitingCertificateConfirmation => _isAwaitingCertificateConfirmation;
        public bool IsPaused
        {
            get => _isPaused;
            set => _isPaused = value;
        }

        public HashSet<string> ExcludedDomains { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExcludedProcesses { get; } = new(StringComparer.OrdinalIgnoreCase);

        // SSL Passthrough: domains where TLS is NOT intercepted (no MITM),
        // but CONNECT requests are still logged with metadata (host, port, process, timestamp).
        // Use this for sites protected by Cloudflare or similar that block MITM proxies.
        public HashSet<string> SslPassthroughDomains { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Auto-responder rules
        public ObservableCollection<AutoResponderRule> AutoResponderRules { get; } = new();
        public bool AutoResponderEnabled { get; set; }

        public void Start()
        {
            if (_isRunning) return;

            // The Windows prompt to trust a new root certificate pumps the UI thread's messages,
            // so another Start can run inside it; that one would open a second prompt.
            if (_isAwaitingCertificateConfirmation)
                throw new InvalidOperationException(CertificateConfirmationMessage);

            try
            {
                _proxyServer = new ProxyServer();

                _proxyServer.CertificateManager.RootCertificateIssuerName = "HttpTrafficMonitor CA";
                _proxyServer.CertificateManager.RootCertificateName = "HttpTrafficMonitor Root Certificate";
                _proxyServer.CertificateManager.SaveFakeCertificates = true;

                _proxyServer.CertificateManager.CreateRootCertificate();
                _isAwaitingCertificateConfirmation = !IsTrustedRootCertificate(_proxyServer.CertificateManager.RootCertificate);
                try
                {
                    _proxyServer.CertificateManager.EnsureRootCertificate();
                    // Ask only once: Start and SetAsSystemHttpsProxy ensure the root certificate again
                    // and would repeat the prompt after a "No".
                    _proxyServer.CertificateManager.EnsureRootCertificate(userTrustRootCertificate: false, machineTrustRootCertificate: false);
                    _proxyServer.CertificateManager.TrustRootCertificate(true);
                }
                finally
                {
                    _isAwaitingCertificateConfirmation = false;
                }

                _proxyServer.EnableConnectionPool = false;

                _proxyServer.BeforeRequest += OnBeforeRequest;
                _proxyServer.BeforeResponse += OnBeforeResponse;
                _proxyServer.ServerCertificateValidationCallback += OnServerCertificateValidation;
                _proxyServer.ExceptionFunc = OnProxyException;

                _explicitEndPoint = new ExplicitProxyEndPoint(IPAddress.Loopback, ProxyPort, true);
                _explicitEndPoint.BeforeTunnelConnectRequest += OnBeforeTunnelConnectRequest;
                _proxyServer.AddEndPoint(_explicitEndPoint);

                _proxyServer.Start();

                _proxyServer.SetAsSystemHttpProxy(_explicitEndPoint);
                _proxyServer.SetAsSystemHttpsProxy(_explicitEndPoint);

                _isRunning = true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Failed to start proxy: {ex.Message}");
                Stop();
                throw;
            }
        }

        public void Stop()
        {
            if (_proxyServer == null) return;

            try
            {
                if (_isRunning)
                    _proxyServer.DisableAllSystemProxies();
            }
            catch { }

            try
            {
                if (_explicitEndPoint != null)
                    _explicitEndPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnectRequest;
                _proxyServer.BeforeRequest -= OnBeforeRequest;
                _proxyServer.BeforeResponse -= OnBeforeResponse;
                _proxyServer.ServerCertificateValidationCallback -= OnServerCertificateValidation;
                _proxyServer.Stop();
            }
            catch { }

            _isRunning = false;
            _proxyServer = null;
            _explicitEndPoint = null;

            lock (_lock)
            {
                _processNameCache.Clear();
            }
        }

        private async Task OnBeforeRequest(object sender, SessionEventArgs e)
        {
            if (_isPaused) return;

            try
            {
                string host = e.HttpClient.Request.Host ?? string.Empty;
                if (ExcludedDomains.Any(d => host.Contains(d, StringComparison.OrdinalIgnoreCase)))
                    return;

                string processName = GetProcessName(e);
                if (ExcludedProcesses.Contains(processName))
                    return;

                // Auto-responder check
                if (AutoResponderEnabled)
                {
                    var matchingRule = AutoResponderRules.FirstOrDefault(r =>
                        r.Matches(e.HttpClient.Request.Url, e.HttpClient.Request.Method));

                    if (matchingRule != null)
                    {
                        await HandleAutoResponse(e, matchingRule);
                        // Still capture the request for display
                    }
                }

                int id = Interlocked.Increment(ref _requestCounter);
                string requestBody = string.Empty;

                // Detect WebSocket upgrade
                bool isWebSocket = e.HttpClient.Request.Headers
                    .Any(h => h.Name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)
                           && h.Value.Contains("websocket", StringComparison.OrdinalIgnoreCase));

                if (e.HttpClient.Request.HasBody)
                {
                    try
                    {
                        byte[] bodyBytes = await e.GetRequestBody();
                        if (bodyBytes != null && bodyBytes.Length > 0)
                        {
                            if (bodyBytes.Length <= FormatHelper.MaxBodyCaptureSize)
                                requestBody = Encoding.UTF8.GetString(bodyBytes);
                            else
                                requestBody = $"[Body too large: {FormatHelper.FormatSize(bodyBytes.Length)}]";
                        }
                    }
                    catch (Exception ex)
                    {
                        requestBody = $"[Error reading body: {ex.Message}]";
                    }
                }
                else if (e.HttpClient.Request.ContentLength > 0)
                {
                    // Fallback: HasBody may be false but ContentLength is set
                    try
                    {
                        byte[] bodyBytes = await e.GetRequestBody();
                        if (bodyBytes != null && bodyBytes.Length > 0 && bodyBytes.Length <= FormatHelper.MaxBodyCaptureSize)
                            requestBody = Encoding.UTF8.GetString(bodyBytes);
                    }
                    catch { }
                }

                var entry = new HttpRequestEntry
                {
                    Id = id,
                    Timestamp = DateTime.Now,
                    ProcessName = processName,
                    ProcessId = GetProcessId(e),
                    Method = e.HttpClient.Request.Method,
                    Url = e.HttpClient.Request.Url,
                    Host = host,
                    Scheme = e.HttpClient.Request.IsHttps ? "https" : "http",
                    RequestHeaders = FormatRequestHeaders(e.HttpClient.Request),
                    RequestBody = FormatHelper.FormatBody(requestBody, e.HttpClient.Request.ContentType),
                    RequestContentType = e.HttpClient.Request.ContentType ?? string.Empty,
                    IsWebSocket = isWebSocket,
                };

                // Attach TLS certificate info if HTTPS
                if (e.HttpClient.Request.IsHttps && _tlsCertCache.TryGetValue(host, out var tlsInfo))
                    entry.TlsInfo = tlsInfo;

                e.UserData = entry;
                RequestCaptured?.Invoke(entry);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Error capturing request: {ex.Message}");
            }
        }

        private async Task OnBeforeResponse(object sender, SessionEventArgs e)
        {
            if (_isPaused) return;
            if (e.UserData is not HttpRequestEntry entry) return;

            try
            {
                entry.StatusCode = e.HttpClient.Response.StatusCode;
                entry.ResponseContentType = e.HttpClient.Response.ContentType ?? string.Empty;
                entry.ResponseHeaders = FormatResponseHeaders(e.HttpClient.Response);
                entry.ResponseTime = DateTime.Now;
                entry.Duration = entry.ResponseTime.Value - entry.Timestamp;

                // Mark slow requests
                if (entry.Duration.Value.TotalMilliseconds > SlowRequestThresholdMs)
                    entry.IsSlow = true;

                if (e.HttpClient.Response.HasBody)
                {
                    try
                    {
                        byte[] bodyBytes = await e.GetResponseBody();
                        entry.ResponseSize = bodyBytes?.Length ?? 0;

                        if (bodyBytes != null && bodyBytes.Length > 0)
                        {
                            if (bodyBytes.Length <= FormatHelper.MaxBodyCaptureSize)
                            {
                                string bodyText = Encoding.UTF8.GetString(bodyBytes);
                                entry.ResponseBody = FormatHelper.FormatBody(bodyText, e.HttpClient.Response.ContentType);
                            }
                            else
                            {
                                entry.ResponseBody = $"[Body too large: {FormatHelper.FormatSize(bodyBytes.Length)}]";
                                entry.ResponseSize = bodyBytes.Length;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        entry.ResponseBody = $"[Error reading body: {ex.Message}]";
                    }
                }

                entry.IsComplete = true;
                ResponseUpdated?.Invoke(entry);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Error capturing response: {ex.Message}");
            }
        }

        private Task OnServerCertificateValidation(object sender, CertificateValidationEventArgs e)
        {
            // Capture TLS certificate information
            try
            {
                if (e.Certificate != null)
                {
                    var cert = new X509Certificate2(e.Certificate);
                    string host = cert.GetNameInfo(X509NameType.DnsName, false) ?? "unknown";

                    int keySize = 0;
                    try { keySize = cert.PublicKey.GetRSAPublicKey()?.KeySize ?? cert.PublicKey.GetECDsaPublicKey()?.KeySize ?? 0; }
                    catch { }

                    var info = new TlsCertificateInfo
                    {
                        Subject = cert.Subject,
                        Issuer = cert.Issuer,
                        NotBefore = cert.NotBefore,
                        NotAfter = cert.NotAfter,
                        SerialNumber = cert.SerialNumber,
                        Thumbprint = cert.Thumbprint,
                        SignatureAlgorithm = cert.SignatureAlgorithm.FriendlyName ?? "",
                        KeySize = keySize,
                        HasErrors = e.SslPolicyErrors != System.Net.Security.SslPolicyErrors.None,
                        ErrorSummary = e.SslPolicyErrors.ToString(),
                    };

                    // Build certificate chain
                    using (var chain = new System.Security.Cryptography.X509Certificates.X509Chain())
                    {
                        chain.Build(cert);
                        foreach (var element in chain.ChainElements)
                        {
                            info.Chain.Add(new CertificateChainEntry
                            {
                                Subject = element.Certificate.Subject,
                                Issuer = element.Certificate.Issuer,
                                Thumbprint = element.Certificate.Thumbprint,
                                NotAfter = element.Certificate.NotAfter
                            });
                        }
                    }

                    _tlsCertCache[host] = info;

                    // Also cache by the request host header if different
                    if (e.Certificate is X509Certificate2 c2)
                    {
                        foreach (var san in c2.Extensions)
                        {
                            // Just cache by Subject CN for lookup
                        }
                    }
                }
            }
            catch { }

            e.IsValid = true;
            return Task.CompletedTask;
        }

        private async Task HandleAutoResponse(SessionEventArgs e, AutoResponderRule rule)
        {
            try
            {
                if (rule.DelayMs > 0)
                    await Task.Delay(rule.DelayMs);

                string body;
                if (!string.IsNullOrEmpty(rule.ResponseFilePath) && File.Exists(rule.ResponseFilePath))
                    body = await File.ReadAllTextAsync(rule.ResponseFilePath);
                else
                    body = rule.ResponseBody;

                var headers = new Dictionary<string, HttpHeader>();
                foreach (string line in rule.ResponseHeaders.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    int colonIdx = line.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        string key = line[..colonIdx].Trim();
                        string value = line[(colonIdx + 1)..].Trim();
                        headers[key] = new HttpHeader(key, value);
                    }
                }

                e.Ok(body, headers, true);
                rule.MatchCount++;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Auto-responder error: {ex.Message}");
            }
        }

        private Task OnBeforeTunnelConnectRequest(object sender, Titanium.Web.Proxy.EventArguments.TunnelConnectSessionEventArgs e)
        {
            string host = e.HttpClient.Request.RequestUri.Host;

            if (SslPassthroughDomains.Any(d => host.Contains(d, StringComparison.OrdinalIgnoreCase)))
            {
                // Disable MITM for this domain — traffic passes through as raw TCP tunnel.
                // Cloudflare and similar services won't detect the proxy.
                e.DecryptSsl = false;

                if (!_isPaused)
                {
                    // Still log the CONNECT request with available metadata
                    int id = Interlocked.Increment(ref _requestCounter);
                    string processName = "Unknown";
                    int processId = 0;
                    try
                    {
                        processId = e.HttpClient.ProcessId.Value;
                        processName = GetProcessNameById(processId);
                    }
                    catch { }

                    var entry = new HttpRequestEntry
                    {
                        Id = id,
                        Timestamp = DateTime.Now,
                        ProcessName = processName,
                        ProcessId = processId,
                        Method = "CONNECT",
                        Url = $"https://{host}:{e.HttpClient.Request.RequestUri.Port}/",
                        Host = host,
                        Scheme = "https",
                        RequestHeaders = $"CONNECT {host}:{e.HttpClient.Request.RequestUri.Port} HTTP/1.1\r\nHost: {host}",
                        RequestBody = string.Empty,
                        StatusCode = 200,
                        ResponseHeaders = "HTTP/1.1 200 Connection Established\r\nX-Ssl-Passthrough: true",
                        ResponseBody = "[SSL Passthrough — TLS not intercepted]",
                        IsComplete = true,
                        IsSslPassthrough = true,
                    };

                    RequestCaptured?.Invoke(entry);
                    ResponseUpdated?.Invoke(entry);
                }
            }

            return Task.CompletedTask;
        }

        private string GetProcessNameById(int pid)
        {
            if (pid <= 0) return "Unknown";

            if (_processNameCache.TryGetValue(pid, out string? cached))
                return cached;

            try
            {
                using var process = Process.GetProcessById(pid);
                string name = process.ProcessName;
                lock (_lock) { _processNameCache[pid] = name; }
                return name;
            }
            catch { return "Unknown"; }
        }

        private void OnProxyException(Exception ex)
        {
            if (ex is ObjectDisposedException) return;
            ErrorOccurred?.Invoke($"Proxy error: {ex.Message}");
        }

        private int GetProcessId(SessionEventArgs e)
        {
            try { return e.HttpClient.ProcessId.Value; }
            catch { return 0; }
        }

        private string GetProcessName(SessionEventArgs e)
        {
            try
            {
                int pid = GetProcessId(e);
                if (pid <= 0) return "Unknown";

                if (DateTime.UtcNow - _lastCacheClear > TimeSpan.FromSeconds(10))
                {
                    _processNameCache.Clear();
                    _lastCacheClear = DateTime.UtcNow;
                }

                if (_processNameCache.TryGetValue(pid, out string? cached))
                    return cached;

                using var process = Process.GetProcessById(pid);
                string name = process.ProcessName;
                _processNameCache[pid] = name;
                return name;
            }
            catch { return "Unknown"; }
        }

        private static string FormatRequestHeaders(Request request)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{request.Method} {request.RequestUri.PathAndQuery} {request.HttpVersion}");
            sb.AppendLine($"Host: {request.Host}");
            foreach (var header in request.Headers)
                sb.AppendLine($"{header.Name}: {header.Value}");
            return sb.ToString().TrimEnd();
        }

        private static string FormatResponseHeaders(Response response)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"HTTP/{response.HttpVersion.Major}.{response.HttpVersion.Minor} {response.StatusCode} {response.StatusDescription}");
            foreach (var header in response.Headers)
                sb.AppendLine($"{header.Name}: {header.Value}");
            return sb.ToString().TrimEnd();
        }

        // CertificateManager.IsRootCertificateUserTrusted matches by name, so an older certificate
        // with the same name would count; only this exact certificate makes the prompt unnecessary.
        private static bool IsTrustedRootCertificate(X509Certificate2? certificate)
        {
            if (certificate == null) return false;

            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count > 0;
        }

        public void RemoveRootCertificate()
        {
            try { _proxyServer?.CertificateManager.RemoveTrustedRootCertificate(); }
            catch { }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
