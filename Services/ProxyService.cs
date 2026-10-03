using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
        private bool _isRootCertificateTrusted;
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
        public const string StoppedDuringConfirmationMessage =
            "The proxy was stopped while the root certificate confirmation was waiting, so it was not started.";
        public const string RootCertificateNotTrustedMessage =
            "Monitoring HTTP traffic only: Windows does not trust the HttpTrafficMonitor root certificate, so HTTPS traffic cannot be decrypted. " +
            "Click Stop, then Start, and answer Yes when Windows asks to install the certificate.";

        public event Action<HttpRequestEntry>? RequestCaptured;
        public event Action<HttpRequestEntry>? ResponseUpdated;
        public event Action<string>? ErrorOccurred;

        public bool IsRunning => _isRunning;
        public bool IsAwaitingCertificateConfirmation => _isAwaitingCertificateConfirmation;
        public string MonitoringStatusMessage => _isRootCertificateTrusted
            ? $"Monitoring traffic on port {ProxyPort}..."
            : RootCertificateNotTrustedMessage;
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
                var proxyServer = new ProxyServer();
                _proxyServer = proxyServer;

                _proxyServer.CertificateManager.RootCertificateIssuerName = "HttpTrafficMonitor CA";
                _proxyServer.CertificateManager.RootCertificateName = "HttpTrafficMonitor Root Certificate";
                _proxyServer.CertificateManager.SaveFakeCertificates = true;

                _proxyServer.CertificateManager.CreateRootCertificate();
                _isAwaitingCertificateConfirmation = !IsTrustedRootCertificate(_proxyServer.CertificateManager.RootCertificate);
                try
                {
                    proxyServer.CertificateManager.EnsureRootCertificate();
                    // Ask only once: Start and SetAsSystemHttpsProxy ensure the root certificate again
                    // and would repeat the prompt after a "No".
                    proxyServer.CertificateManager.EnsureRootCertificate(userTrustRootCertificate: false, machineTrustRootCertificate: false);
                    proxyServer.CertificateManager.TrustRootCertificate(true);
                }
                finally
                {
                    _isAwaitingCertificateConfirmation = false;
                }

                // A refused prompt is reported by Titanium only to its default handler, so check the result instead.
                _isRootCertificateTrusted = IsTrustedRootCertificate(proxyServer.CertificateManager.RootCertificate);

                // Stop can run inside the prompt as well; it has already dropped this start's server.
                if (_proxyServer != proxyServer)
                    throw new InvalidOperationException(StoppedDuringConfirmationMessage);

                _proxyServer.EnableConnectionPool = false;
                // A prefetched server connection is opened for the CONNECT, so its DNS, TCP and TLS
                // steps would be missing from the request's timing breakdown.
                _proxyServer.EnableTcpServerConnectionPrefetch = false;

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
            catch (Exception ex) when (IsPortInUse(ex))
            {
                // Titanium only says that the endpoint failed to start; the cause is on the inner exception.
                // It binds with ReuseAddress, so a port held by another program fails with AccessDenied.
                var portInUse = new InvalidOperationException(
                    $"Port {ProxyPort} is already in use or reserved by another program. Close that program, or the other running HttpTrafficMonitor, and click Start again.", ex);
                ErrorOccurred?.Invoke($"Failed to start proxy: {portInUse.Message}");
                Stop();
                throw portInUse;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Failed to start proxy: {ex.Message}");
                Stop();
                throw;
            }
        }

        private static bool IsPortInUse(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse or SocketError.AccessDenied })
                    return true;
            }
            return false;
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
                var matchingRule = AutoResponderEnabled
                    ? AutoResponderRules.FirstOrDefault(r => r.Matches(e.HttpClient.Request.Url, e.HttpClient.Request.Method))
                    : null;

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

                if (matchingRule != null)
                    await HandleAutoResponse(e, matchingRule, entry);
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

                SetTimingBreakdown(entry, e, DateTime.UtcNow);

                // The server certificate of a new connection is only known once the request has been sent.
                if (entry.TlsInfo == null && e.HttpClient.Request.IsHttps && _tlsCertCache.TryGetValue(entry.Host, out var tlsInfo))
                    entry.TlsInfo = tlsInfo;

                entry.IsComplete = true;
                ResponseUpdated?.Invoke(entry);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Error capturing response: {ex.Message}");
            }
        }

        // Titanium stamps each step of a session in UTC; the phases are the gaps between those stamps.
        private static void SetTimingBreakdown(HttpRequestEntry entry, SessionEventArgs e, DateTime responseReadUtc)
        {
            var timeLine = e.TimeLine;
            bool isHttps = e.HttpClient.Request.IsHttps;

            if (timeLine.TryGetValue("Connection Established", out var connected))
            {
                if (timeLine.TryGetValue("Dns Resolved", out var dnsResolved))
                {
                    entry.DnsLookupMs = ElapsedMs(entry.Timestamp.ToUniversalTime(), dnsResolved);
                    entry.TcpConnectMs = ElapsedMs(dnsResolved, connected);
                }
                if (isHttps && timeLine.TryGetValue("HTTPS Established", out var tlsEstablished))
                    entry.TlsHandshakeMs = ElapsedMs(connected, tlsEstablished);
            }
            else if (timeLine.ContainsKey("Connection Ready"))
            {
                // A kept-alive server connection was reused: no lookup, connect or handshake for this request.
                entry.DnsLookupMs = 0;
                entry.TcpConnectMs = 0;
                if (isHttps)
                    entry.TlsHandshakeMs = 0;
            }

            if (timeLine.TryGetValue("Request Sent", out var requestSent)
                && timeLine.TryGetValue("Response Received", out var responseReceived))
            {
                entry.TimeToFirstByteMs = ElapsedMs(requestSent, responseReceived);
                entry.ContentDownloadMs = ElapsedMs(responseReceived, responseReadUtc);
            }
        }

        private static double ElapsedMs(DateTime fromUtc, DateTime toUtc)
            => Math.Max(0, (toUtc - fromUtc).TotalMilliseconds);

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

        private async Task HandleAutoResponse(SessionEventArgs e, AutoResponderRule rule, HttpRequestEntry entry)
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

                e.GenericResponse(body, (HttpStatusCode)rule.ResponseStatusCode, headers, true);
                rule.MatchCount++;

                // BeforeResponse is not raised for a response set here, so the entry is completed now.
                var response = e.HttpClient.Response;
                byte[] bodyBytes = response.Encoding.GetBytes(body);

                entry.StatusCode = response.StatusCode;
                entry.ResponseContentType = response.ContentType ?? string.Empty;
                entry.ResponseHeaders = FormatResponseHeaders(response);
                entry.ResponseTime = DateTime.Now;
                entry.Duration = entry.ResponseTime.Value - entry.Timestamp;

                if (entry.Duration.Value.TotalMilliseconds > SlowRequestThresholdMs)
                    entry.IsSlow = true;

                entry.ResponseSize = bodyBytes.Length;
                if (bodyBytes.Length <= FormatHelper.MaxBodyCaptureSize)
                    entry.ResponseBody = FormatHelper.FormatBody(body, response.ContentType);
                else
                    entry.ResponseBody = $"[Body too large: {FormatHelper.FormatSize(bodyBytes.Length)}]";

                entry.IsComplete = true;
                ResponseUpdated?.Invoke(entry);
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
            if (IsDroppedConnection(ex)) return;
            ErrorOccurred?.Invoke($"Proxy error: {ex.Message}");
        }

        // For a server that cannot be reached and for a client that closes the connection during the
        // TLS handshake (for example because it rejects the certificate), Titanium drops the socket
        // error and keeps only its own message.
        private static readonly string[] DroppedConnectionMessages =
        {
            "Could not establish connection to ",
            "Stream is already closed"
        };

        // Titanium reports every connection that a client closes or a server refuses, wrapped as
        // "Connection was aborted" or "Error occured whilst handling session request"; that is
        // routine traffic, not a failure of the proxy.
        private static bool IsDroppedConnection(Exception ex)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e is ObjectDisposedException || e is OperationCanceledException || e is IOException || e is SocketException)
                    return true;
                if (DroppedConnectionMessages.Any(m => e.Message.StartsWith(m, StringComparison.Ordinal)))
                    return true;
            }
            return false;
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
