using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using HttpTrafficMonitor.Models;
using HttpTrafficMonitor.ViewModels;

namespace HttpTrafficMonitor.Services
{
    public class IpcApiService : IDisposable
    {
        private readonly MainViewModel _vm;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Thread? _listenerThread;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public IpcApiService(MainViewModel vm)
        {
            _vm = vm;
        }

        public void Start()
        {
            if (_listener != null) return;

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://localhost:18081/api/");

            try
            {
                _listener.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IPC API failed to start: {ex.Message}");
                _listener = null;
                return;
            }

            _listenerThread = new Thread(ListenerLoop)
            {
                IsBackground = true,
                Name = "IpcApiListener"
            };
            _listenerThread.Start();
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
            _listenerThread = null;
        }

        private void ListenerLoop()
        {
            while (_cts != null && !_cts.IsCancellationRequested)
            {
                HttpListenerContext? ctx = null;
                try
                {
                    ctx = _listener?.GetContext();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    continue;
                }

                if (ctx != null)
                {
                    Task.Run(() => HandleRequest(ctx));
                }
            }
        }

        private T InvokeOnUI<T>(Func<T> action)
        {
            return Application.Current.Dispatcher.Invoke(action);
        }

        private void InvokeOnUI(Action action)
        {
            Application.Current.Dispatcher.Invoke(action);
        }

        private static void WriteJson(HttpListenerResponse response, object data, int statusCode = 200)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            string json = JsonSerializer.Serialize(data, JsonOpts);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }

        private static void WriteJsonString(HttpListenerResponse response, string json, int statusCode = 200)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }

        private static void WriteError(HttpListenerResponse response, string message, int statusCode = 400)
        {
            WriteJson(response, new { error = message }, statusCode);
        }

        private static T ReadBody<T>(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            string body = reader.ReadToEnd();
            return JsonSerializer.Deserialize<T>(body, JsonOpts)
                ?? throw new InvalidOperationException("Failed to deserialize request body.");
        }

        private static string ReadBodyRaw(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            return reader.ReadToEnd();
        }

        private static void SetCorsHeaders(HttpListenerResponse response)
        {
            response.Headers.Set("Access-Control-Allow-Origin", "*");
            response.Headers.Set("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            response.Headers.Set("Access-Control-Allow-Headers", "Content-Type, Accept");
        }

        private async Task HandleRequest(HttpListenerContext ctx)
        {
            using var response = ctx.Response;
            SetCorsHeaders(response);

            try
            {
                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 204;
                    return;
                }

                string path = ctx.Request.Url?.AbsolutePath ?? "";
                string method = ctx.Request.HttpMethod;

                // Remove trailing slash
                if (path.Length > 1 && path.EndsWith("/"))
                    path = path.TrimEnd('/');

                // Route the request
                if (path == "/api/proxy/status" && method == "GET")
                    HandleProxyStatus(response);
                else if (path == "/api/proxy/start" && method == "POST")
                    HandleProxyStart(response);
                else if (path == "/api/proxy/stop" && method == "POST")
                    HandleProxyStop(response);
                else if (path == "/api/proxy/pause" && method == "POST")
                    HandleProxyPause(response);
                else if (path == "/api/proxy/resume" && method == "POST")
                    HandleProxyResume(response);
                else if (path == "/api/requests" && method == "GET")
                    HandleGetRequests(ctx.Request, response);
                else if (path == "/api/requests" && method == "DELETE")
                    HandleDeleteRequests(response);
                else if (path == "/api/requests/filter" && method == "POST")
                    HandleFilterRequests(ctx.Request, response);
                else if (path.StartsWith("/api/requests/") && path.EndsWith("/tls") && method == "GET")
                    HandleGetTls(path, response);
                else if (path.StartsWith("/api/requests/") && path.EndsWith("/websocket") && method == "GET")
                    HandleGetWebSocket(path, response);
                else if (path.StartsWith("/api/requests/") && method == "GET")
                    HandleGetRequestById(path, response);
                else if (path == "/api/filter-presets" && method == "GET")
                    HandleGetFilterPresets(response);
                else if (path == "/api/filter-presets" && method == "POST")
                    HandleAddFilterPreset(ctx.Request, response);
                else if (path.StartsWith("/api/filter-presets/") && method == "DELETE")
                    HandleDeleteFilterPreset(path, response);
                else if (path == "/api/exclusions" && method == "GET")
                    HandleGetExclusions(response);
                else if (path == "/api/exclusions" && method == "POST")
                    HandleSetExclusions(ctx.Request, response);
                else if (path == "/api/ssl-passthrough" && method == "GET")
                    HandleGetSslPassthrough(response);
                else if (path == "/api/ssl-passthrough" && method == "POST")
                    HandleSetSslPassthrough(ctx.Request, response);
                else if (path == "/api/alerts/rules" && method == "GET")
                    HandleGetAlertRules(response);
                else if (path == "/api/alerts/rules" && method == "POST")
                    HandleAddAlertRule(ctx.Request, response);
                else if (path.StartsWith("/api/alerts/rules/") && method == "DELETE")
                    HandleDeleteAlertRule(path, response);
                else if (path == "/api/alerts/events" && method == "GET")
                    HandleGetAlertEvents(ctx.Request, response);
                else if (path == "/api/alerts/sound" && method == "POST")
                    HandleSetAlertSound(ctx.Request, response);
                else if (path == "/api/auto-responder/status" && method == "GET")
                    HandleAutoResponderStatus(response);
                else if (path == "/api/auto-responder/toggle" && method == "POST")
                    HandleAutoResponderToggle(ctx.Request, response);
                else if (path == "/api/auto-responder/rules" && method == "GET")
                    HandleGetAutoResponderRules(response);
                else if (path == "/api/auto-responder/rules" && method == "POST")
                    HandleAddAutoResponderRule(ctx.Request, response);
                else if (path.StartsWith("/api/auto-responder/rules/") && method == "PUT")
                    HandleUpdateAutoResponderRule(path, ctx.Request, response);
                else if (path.StartsWith("/api/auto-responder/rules/") && method == "DELETE")
                    HandleDeleteAutoResponderRule(path, response);
                else if (path == "/api/replay" && method == "POST")
                    await HandleReplay(ctx.Request, response);
                else if (path.StartsWith("/api/export/curl/") && method == "GET")
                    HandleExportCurl(path, response);
                else if (path == "/api/export/har" && method == "POST")
                    HandleExportHar(ctx.Request, response);
                else if (path == "/api/export/postman" && method == "POST")
                    HandleExportPostman(ctx.Request, response);
                else if (path == "/api/export/json" && method == "POST")
                    HandleExportJson(ctx.Request, response);
                else if (path == "/api/export/csv" && method == "POST")
                    HandleExportCsv(ctx.Request, response);
                else if (path == "/api/sessions/save" && method == "POST")
                    HandleSessionSave(ctx.Request, response);
                else if (path == "/api/sessions/load" && method == "POST")
                    HandleSessionLoad(ctx.Request, response);
                else if (path == "/api/sessions/recent" && method == "GET")
                    HandleSessionRecent(response);
                else if (path == "/api/content/decode" && method == "POST")
                    HandleContentDecode(ctx.Request, response);
                else if (path == "/api/content/encode" && method == "POST")
                    HandleContentEncode(ctx.Request, response);
                else if (path == "/api/stats" && method == "GET")
                    HandleGetStats(response);
                else if (path == "/api/domains" && method == "GET")
                    HandleGetDomains(response);
                else if (path == "/api/compare" && method == "POST")
                    HandleCompare(ctx.Request, response);
                else if (path.StartsWith("/api/bookmarks/") && path.EndsWith("/toggle") && method == "POST")
                    HandleToggleBookmark(path, ctx.Request, response);
                else if (path == "/api/bookmarks" && method == "GET")
                    HandleGetBookmarks(ctx.Request, response);
                else
                    WriteError(response, $"Unknown endpoint: {method} {path}", 404);
            }
            catch (Exception ex)
            {
                try { WriteError(response, ex.Message, 500); } catch { }
            }
        }

        // ======================= PROXY =======================

        private void HandleProxyStatus(HttpListenerResponse response)
        {
            var data = InvokeOnUI(() => new
            {
                isRunning = _vm.IsMonitoring,
                isPaused = _vm.IsPaused,
                port = ProxyService.ProxyPort,
                requestCount = _vm.TotalRequests
            });
            WriteJson(response, data);
        }

        private void HandleProxyStart(HttpListenerResponse response)
        {
            var proxy = _vm.ProxyServiceInstance;
            string? error = null;

            // Not InvokeOnUI: a prompt to trust the root certificate holds the UI thread until the
            // user answers it, so the caller is told about the prompt instead of waiting for it.
            var start = Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (proxy.IsAwaitingCertificateConfirmation) return;

                try
                {
                    proxy.AutoResponderEnabled = _vm.AutoResponderVm.IsEnabled;
                    proxy.Start();
                    _vm.IsMonitoring = true;
                    _vm.IsPaused = false;
                    _vm.StatusMessage = proxy.MonitoringStatusMessage;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    _vm.IsMonitoring = false;
                    _vm.StatusMessage = $"Failed to start monitoring: {ex.Message}";
                }
            });

            while (!start.Task.Wait(TimeSpan.FromSeconds(1)) && !proxy.IsAwaitingCertificateConfirmation) { }

            if (proxy.IsAwaitingCertificateConfirmation)
                WriteJson(response, new { success = false, awaitingConfirmation = true, message = ProxyService.CertificateConfirmationMessage });
            else if (error != null)
                WriteError(response, error, 500);
            else
                WriteJson(response, new { success = true });
        }

        private void HandleProxyStop(HttpListenerResponse response)
        {
            InvokeOnUI(() => _vm.ExecuteStop());
            WriteJson(response, new { success = true });
        }

        private void HandleProxyPause(HttpListenerResponse response)
        {
            InvokeOnUI(() => _vm.IsPaused = true);
            WriteJson(response, new { success = true });
        }

        private void HandleProxyResume(HttpListenerResponse response)
        {
            InvokeOnUI(() => _vm.IsPaused = false);
            WriteJson(response, new { success = true });
        }

        // ======================= REQUESTS =======================

        private void HandleGetRequests(HttpListenerRequest request, HttpListenerResponse response)
        {
            var qs = request.QueryString;
            int skip = int.TryParse(qs["skip"], out var s) ? s : 0;
            int take = int.TryParse(qs["take"], out var t) ? t : 50;
            string? methodFilter = qs["method"];
            int? statusMin = int.TryParse(qs["statusMin"], out var smin) ? smin : null;
            int? statusMax = int.TryParse(qs["statusMax"], out var smax) ? smax : null;
            string? domain = qs["domain"];
            string? process = qs["process"];
            string? search = qs["search"];
            bool bookmarkedOnly = bool.TryParse(qs["bookmarkedOnly"], out var bm) && bm;
            var redactor = NewRedactor();

            var result = InvokeOnUI(() =>
            {
                List<HttpRequestEntry> snapshot;
                lock (_vm.CollectionLock)
                {
                    snapshot = _vm.AllRequests.ToList();
                }

                IEnumerable<HttpRequestEntry> filtered = snapshot;

                if (!string.IsNullOrEmpty(methodFilter))
                    filtered = filtered.Where(r => r.Method.Equals(methodFilter, StringComparison.OrdinalIgnoreCase));
                if (statusMin.HasValue)
                    filtered = filtered.Where(r => r.StatusCode.HasValue && r.StatusCode.Value >= statusMin.Value);
                if (statusMax.HasValue)
                    filtered = filtered.Where(r => r.StatusCode.HasValue && r.StatusCode.Value <= statusMax.Value);
                if (!string.IsNullOrEmpty(domain))
                    filtered = filtered.Where(r => r.Host.Contains(domain, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(process))
                    filtered = filtered.Where(r => r.ProcessName.Contains(process, StringComparison.OrdinalIgnoreCase));
                if (bookmarkedOnly)
                    filtered = filtered.Where(r => r.IsBookmarked);
                // Match the URL the agent is shown, so a search cannot guess a hidden token
                var searchRedactor = NewRedactor();
                if (!string.IsNullOrEmpty(search))
                    filtered = filtered.Where(r =>
                        searchRedactor.Redact(r.Url).Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        r.Host.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        r.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        r.Method.Contains(search, StringComparison.OrdinalIgnoreCase));

                var filteredList = filtered.ToList();
                int totalCount = filteredList.Count;
                var page = filteredList.Skip(skip).Take(take).ToList();

                return new
                {
                    requests = page.Select(e => MapRequestSummary(e, redactor)).ToArray(),
                    totalCount,
                    credentialsNotice = redactor.NoticeIfRedacted
                };
            });

            WriteJson(response, result);
        }

        private void HandleGetRequestById(string path, HttpListenerResponse response)
        {
            // path: /api/requests/{id}
            string idStr = path.Substring("/api/requests/".Length);
            if (!int.TryParse(idStr, out int id))
            {
                WriteError(response, "Invalid request ID.");
                return;
            }

            var result = InvokeOnUI(() =>
            {
                HttpRequestEntry? entry;
                lock (_vm.CollectionLock)
                {
                    entry = _vm.AllRequests.FirstOrDefault(r => r.Id == id);
                }
                return entry;
            });

            if (result == null)
            {
                WriteError(response, "Request not found.", 404);
                return;
            }

            var detail = InvokeOnUI(() => MapRequestDetail(result, NewRedactor()));
            WriteJson(response, detail);
        }

        private void HandleDeleteRequests(HttpListenerResponse response)
        {
            InvokeOnUI(() =>
            {
                lock (_vm.CollectionLock) { _vm.AllRequests.Clear(); }
                _vm.TotalRequests = 0;
                _vm.GetCount = 0;
                _vm.PostCount = 0;
                _vm.PutCount = 0;
                _vm.DeleteCount = 0;
                _vm.ErrorCount = 0;
                _vm.TotalDataTransferred = 0;
                _vm.SelectedRequest = null;
                _vm.GraphsVm.Reset();
                _vm.StatusMessage = _vm.IsMonitoring ? "Logs cleared. Still monitoring..." : "Logs cleared.";
            });
            WriteJson(response, new { success = true });
        }

        private void HandleFilterRequests(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<FilterRequestBody>(request);
            var redactor = NewRedactor();

            var result = InvokeOnUI(() =>
            {
                List<HttpRequestEntry> snapshot;
                lock (_vm.CollectionLock)
                {
                    snapshot = _vm.AllRequests.ToList();
                }

                var preset = new FilterPreset
                {
                    LogicOperator = body.LogicOperator ?? "AND",
                    Conditions = (body.Conditions ?? new List<FilterConditionDto>()).Select(c => new FilterCondition
                    {
                        Field = c.Field ?? "URL",
                        Operator = c.Operator ?? "Contains",
                        Value = c.Value ?? ""
                    }).ToList()
                };

                // Evaluate what the agent is shown, so a condition cannot guess a hidden value
                var matchRedactor = NewRedactor();
                var filtered = snapshot.Where(e => preset.Evaluate(AsShownToAgent(e, matchRedactor))).ToList();
                int totalCount = filtered.Count;
                int skip = body.Skip ?? 0;
                int take = body.Take ?? 50;
                var page = filtered.Skip(skip).Take(take).ToList();

                return new
                {
                    requests = page.Select(e => MapRequestSummary(e, redactor)).ToArray(),
                    totalCount,
                    credentialsNotice = redactor.NoticeIfRedacted
                };
            });

            WriteJson(response, result);
        }

        // ======================= FILTER PRESETS =======================

        private void HandleGetFilterPresets(HttpListenerResponse response)
        {
            var presets = InvokeOnUI(() =>
                _vm.AdvancedFilterVm.Presets.Select(p => new
                {
                    name = p.Name,
                    logicOperator = p.LogicOperator,
                    conditions = p.Conditions.Select(c => new
                    {
                        field = c.Field,
                        @operator = c.Operator,
                        value = c.Value
                    }).ToArray()
                }).ToArray()
            );
            WriteJson(response, presets);
        }

        private void HandleAddFilterPreset(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<FilterPresetDto>(request);

            InvokeOnUI(() =>
            {
                var preset = new FilterPreset
                {
                    Name = body.Name ?? "Unnamed",
                    LogicOperator = body.LogicOperator ?? "AND",
                    Conditions = (body.Conditions ?? new List<FilterConditionDto>()).Select(c => new FilterCondition
                    {
                        Field = c.Field ?? "URL",
                        Operator = c.Operator ?? "Contains",
                        Value = c.Value ?? ""
                    }).ToList()
                };

                var existing = _vm.AdvancedFilterVm.Presets.FirstOrDefault(p => p.Name == preset.Name);
                if (existing != null)
                    _vm.AdvancedFilterVm.Presets.Remove(existing);
                _vm.AdvancedFilterVm.Presets.Add(preset);
            });
            WriteJson(response, new { success = true });
        }

        private void HandleDeleteFilterPreset(string path, HttpListenerResponse response)
        {
            string name = Uri.UnescapeDataString(path.Substring("/api/filter-presets/".Length));

            bool found = InvokeOnUI(() =>
            {
                var preset = _vm.AdvancedFilterVm.Presets.FirstOrDefault(p =>
                    p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (preset != null)
                {
                    _vm.AdvancedFilterVm.Presets.Remove(preset);
                    return true;
                }
                return false;
            });

            if (!found)
            {
                WriteError(response, "Preset not found.", 404);
                return;
            }
            WriteJson(response, new { success = true });
        }

        // ======================= EXCLUSIONS =======================

        private void HandleGetExclusions(HttpListenerResponse response)
        {
            var data = InvokeOnUI(() => new
            {
                domains = _vm.ProxyServiceInstance.ExcludedDomains.ToArray(),
                processes = _vm.ProxyServiceInstance.ExcludedProcesses.ToArray()
            });
            WriteJson(response, data);
        }

        private void HandleSetExclusions(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<ExclusionsDto>(request);

            InvokeOnUI(() =>
            {
                if (body.Domains != null)
                {
                    _vm.ProxyServiceInstance.ExcludedDomains.Clear();
                    foreach (var d in body.Domains)
                        _vm.ProxyServiceInstance.ExcludedDomains.Add(d);
                    _vm.ExcludedDomainsText = string.Join("\n", body.Domains);
                }
                if (body.Processes != null)
                {
                    _vm.ProxyServiceInstance.ExcludedProcesses.Clear();
                    foreach (var p in body.Processes)
                        _vm.ProxyServiceInstance.ExcludedProcesses.Add(p);
                    _vm.ExcludedProcessesText = string.Join("\n", body.Processes);
                }
            });
            WriteJson(response, new { success = true });
        }

        // ======================= SSL PASSTHROUGH =======================

        private void HandleGetSslPassthrough(HttpListenerResponse response)
        {
            var data = InvokeOnUI(() => new
            {
                domains = _vm.ProxyServiceInstance.SslPassthroughDomains.ToArray()
            });
            WriteJson(response, data);
        }

        private void HandleSetSslPassthrough(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<SslPassthroughDto>(request);

            InvokeOnUI(() =>
            {
                if (body.Domains != null)
                {
                    _vm.ProxyServiceInstance.SslPassthroughDomains.Clear();
                    foreach (var d in body.Domains)
                        _vm.ProxyServiceInstance.SslPassthroughDomains.Add(d);
                    _vm.SslPassthroughDomainsText = string.Join("\n", body.Domains);
                }
            });
            WriteJson(response, new { success = true });
        }

        // ======================= ALERTS =======================

        private void HandleGetAlertRules(HttpListenerResponse response)
        {
            var rules = InvokeOnUI(() =>
                _vm.AlertsVm.Rules.Select(MapAlertRule).ToArray()
            );
            WriteJson(response, rules);
        }

        private void HandleAddAlertRule(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<AlertRuleDto>(request);

            if (string.IsNullOrWhiteSpace(body.Name))
            {
                WriteError(response, "the rule has no name. Give it one: the Alerts tab lists the rule by its name and each alert event names the rule that fired.");
                return;
            }

            // Only a type's name counts, in any letter case; Enum.TryParse alone would also take "7" or "StatusCode,Domain".
            var typeName = Enum.GetNames<AlertRuleType>()
                .FirstOrDefault(n => string.Equals(n, body.Type?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (typeName == null)
            {
                WriteError(response, $"'{body.Type}' is not an alert rule type. Supported types: {string.Join(", ", Enum.GetNames<AlertRuleType>())}.");
                return;
            }
            var ruleType = Enum.Parse<AlertRuleType>(typeName);

            var rule = new AlertRule
            {
                Name = body.Name,
                IsEnabled = body.IsEnabled ?? true,
                Type = ruleType,
                Pattern = body.Pattern ?? string.Empty,
                StatusCodeMin = body.StatusCodeMin,
                StatusCodeMax = body.StatusCodeMax,
                ResponseTimeThresholdMs = body.ResponseTimeThresholdMs,
                SizeThresholdBytes = body.SizeThresholdBytes,
                PlaySound = body.PlaySound ?? false,
                ShowToast = body.ShowToast ?? true
            };

            string? cannotFire = AlertService.FindWhyRuleCannotFire(rule);
            if (cannotFire != null)
            {
                WriteError(response, cannotFire);
                return;
            }

            // Store the range a StatusCode rule fires on, so the rule shows it wherever it is listed
            if (rule.Type == AlertRuleType.StatusCode)
            {
                rule.StatusCodeMin ??= AlertService.DefaultStatusCodeMin;
                rule.StatusCodeMax ??= AlertService.DefaultStatusCodeMax;
            }

            var data = InvokeOnUI(() =>
            {
                _vm.AlertsVm.Rules.Add(rule);
                return MapAlertRule(rule);
            });

            WriteJson(response, data);
        }

        private void HandleDeleteAlertRule(string path, HttpListenerResponse response)
        {
            string id = path.Substring("/api/alerts/rules/".Length);

            bool found = InvokeOnUI(() =>
            {
                var rule = _vm.AlertsVm.Rules.FirstOrDefault(r => r.Id == id);
                if (rule != null)
                {
                    _vm.AlertsVm.Rules.Remove(rule);
                    return true;
                }
                return false;
            });

            if (!found)
            {
                WriteError(response, "Alert rule not found.", 404);
                return;
            }
            WriteJson(response, new { success = true });
        }

        private void HandleGetAlertEvents(HttpListenerRequest request, HttpListenerResponse response)
        {
            var qs = request.QueryString;
            int skip = int.TryParse(qs["skip"], out var s) ? s : 0;
            int take = int.TryParse(qs["take"], out var t) ? t : 50;
            var redactor = NewRedactor();

            var result = InvokeOnUI(() =>
            {
                var events = _vm.AlertsVm.AlertEvents.Skip(skip).Take(take).Select(e => new
                {
                    timestamp = e.Timestamp.ToString("O"),
                    ruleName = e.Rule.Name,
                    ruleType = e.Rule.Type.ToString(),
                    message = redactor.Redact(e.Message),
                    requestId = e.Request.Id,
                    requestUrl = redactor.Redact(e.Request.Url)
                }).ToArray();

                return new
                {
                    events,
                    totalCount = _vm.AlertsVm.AlertEvents.Count,
                    credentialsNotice = redactor.NoticeIfRedacted
                };
            });

            WriteJson(response, result);
        }

        private void HandleSetAlertSound(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<SoundEnabledDto>(request);

            InvokeOnUI(() =>
            {
                _vm.AlertServiceInstance.SoundEnabled = body.Enabled;
                _vm.AlertsVm.SoundEnabled = body.Enabled;
            });
            WriteJson(response, new { success = true });
        }

        // ======================= AUTO-RESPONDER =======================

        private void HandleAutoResponderStatus(HttpListenerResponse response)
        {
            var data = InvokeOnUI(() => new
            {
                enabled = _vm.AutoResponderVm.IsEnabled,
                ruleCount = _vm.AutoResponderVm.Rules.Count
            });
            WriteJson(response, data);
        }

        private void HandleAutoResponderToggle(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<AutoResponderToggleDto>(request);

            InvokeOnUI(() =>
            {
                _vm.AutoResponderVm.IsEnabled = body.Enabled;
                _vm.ProxyServiceInstance.AutoResponderEnabled = body.Enabled;
            });
            WriteJson(response, new { success = true });
        }

        private void HandleGetAutoResponderRules(HttpListenerResponse response)
        {
            var rules = InvokeOnUI(() =>
                _vm.AutoResponderVm.Rules.Select(MapAutoResponderRule).ToArray()
            );
            WriteJson(response, rules);
        }

        private void HandleAddAutoResponderRule(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<AutoResponderRuleDto>(request);

            var data = InvokeOnUI(() =>
            {
                var rule = new AutoResponderRule
                {
                    IsEnabled = body.IsEnabled ?? true,
                    UrlPattern = body.UrlPattern ?? string.Empty,
                    IsRegex = body.IsRegex ?? false,
                    HttpMethod = body.HttpMethod ?? "ANY",
                    ResponseStatusCode = body.ResponseStatusCode ?? 200,
                    ResponseHeaders = body.ResponseHeaders ?? "Content-Type: application/json",
                    ResponseBody = body.ResponseBody ?? string.Empty,
                    ResponseFilePath = body.ResponseFilePath,
                    DelayMs = body.DelayMs ?? 0
                };
                _vm.AutoResponderVm.Rules.Add(rule);
                return MapAutoResponderRule(rule);
            });

            WriteJson(response, data);
        }

        private void HandleUpdateAutoResponderRule(string path, HttpListenerRequest request, HttpListenerResponse response)
        {
            string id = path.Substring("/api/auto-responder/rules/".Length);
            var body = ReadBody<AutoResponderRuleDto>(request);

            var data = InvokeOnUI(() =>
            {
                var rule = _vm.AutoResponderVm.Rules.FirstOrDefault(r => r.Id == id);
                if (rule == null) return null;

                if (body.IsEnabled.HasValue) rule.IsEnabled = body.IsEnabled.Value;
                if (body.UrlPattern != null) rule.UrlPattern = body.UrlPattern;
                if (body.IsRegex.HasValue) rule.IsRegex = body.IsRegex.Value;
                if (body.HttpMethod != null) rule.HttpMethod = body.HttpMethod;
                if (body.ResponseStatusCode.HasValue) rule.ResponseStatusCode = body.ResponseStatusCode.Value;
                if (body.ResponseHeaders != null) rule.ResponseHeaders = body.ResponseHeaders;
                if (body.ResponseBody != null) rule.ResponseBody = body.ResponseBody;
                if (body.ResponseFilePath != null) rule.ResponseFilePath = body.ResponseFilePath;
                if (body.DelayMs.HasValue) rule.DelayMs = body.DelayMs.Value;

                return MapAutoResponderRule(rule);
            });

            if (data == null)
            {
                WriteError(response, "Auto-responder rule not found.", 404);
                return;
            }
            WriteJson(response, data);
        }

        private void HandleDeleteAutoResponderRule(string path, HttpListenerResponse response)
        {
            string id = path.Substring("/api/auto-responder/rules/".Length);

            bool found = InvokeOnUI(() =>
            {
                var rule = _vm.AutoResponderVm.Rules.FirstOrDefault(r => r.Id == id);
                if (rule != null)
                {
                    _vm.AutoResponderVm.Rules.Remove(rule);
                    return true;
                }
                return false;
            });

            if (!found)
            {
                WriteError(response, "Auto-responder rule not found.", 404);
                return;
            }
            WriteJson(response, new { success = true });
        }

        // ======================= REPLAY =======================

        private async Task HandleReplay(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<ReplayRequestDto>(request);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (body.Headers != null)
            {
                foreach (var kvp in body.Headers)
                    headers[kvp.Key] = kvp.Value;
            }

            using var replayService = new HttpReplayService();
            var result = await replayService.SendRequestAsync(
                body.Method ?? "GET",
                body.Url ?? "",
                headers,
                body.Body);

            var redactor = NewRedactor();
            WriteJson(response, new
            {
                statusCode = result.StatusCode,
                responseHeaders = redactor.Redact(result.ResponseHeaders),
                responseBody = result.ResponseBody,
                responseSize = result.ResponseSize,
                durationMs = result.Duration.TotalMilliseconds,
                error = result.Error,
                credentialsNotice = redactor.NoticeIfRedacted
            });
        }

        // ======================= EXPORT =======================

        private void HandleExportCurl(string path, HttpListenerResponse response)
        {
            string idStr = path.Substring("/api/export/curl/".Length);
            if (!int.TryParse(idStr, out int id))
            {
                WriteError(response, "Invalid request ID.");
                return;
            }

            var entry = InvokeOnUI(() =>
            {
                lock (_vm.CollectionLock)
                {
                    return _vm.AllRequests.FirstOrDefault(r => r.Id == id);
                }
            });

            if (entry == null)
            {
                WriteError(response, "Request not found.", 404);
                return;
            }

            var redactor = NewRedactor();
            string curl = ExportService.ToCurl(AsShownToAgent(entry, redactor));
            WriteJson(response, new { curl, credentialsNotice = redactor.NoticeIfRedacted });
        }

        private void HandleExportHar(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<ExportIdsDto>(request);

            var entries = GetRequestsByIds(body.RequestIds, NewRedactor());
            string har = ExportService.ToHar(entries);
            WriteJsonString(response, har);
        }

        private void HandleExportPostman(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<PostmanExportDto>(request);

            var entries = GetRequestsByIds(body.RequestIds, NewRedactor());
            string collectionName = body.CollectionName ?? "Exported Collection";
            string postman = ExportService.ToPostmanCollection(entries, collectionName);
            WriteJsonString(response, postman);
        }

        private void HandleExportJson(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<ExportIdsDto>(request);
            var entries = GetRequestsByIds(body.RequestIds, NewRedactor());

            var data = entries.Select(e => new
            {
                id = e.Id,
                timestamp = e.Timestamp.ToString("O"),
                processName = e.ProcessName,
                processId = e.ProcessId,
                method = e.Method,
                url = e.Url,
                host = e.Host,
                scheme = e.Scheme,
                statusCode = e.StatusCode,
                responseSize = e.ResponseSize,
                durationMs = e.Duration?.TotalMilliseconds,
                requestHeaders = e.RequestHeaders,
                requestBody = e.RequestBody,
                responseHeaders = e.ResponseHeaders,
                responseBody = e.ResponseBody,
                isBookmarked = e.IsBookmarked,
                bookmarkNotes = e.BookmarkNotes
            }).ToArray();

            WriteJson(response, data);
        }

        private void HandleExportCsv(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<ExportIdsDto>(request);
            var redactor = NewRedactor();
            var entries = GetRequestsByIds(body.RequestIds, redactor);

            string csv = ExportService.ToCsv(entries);
            WriteJson(response, new { csv, credentialsNotice = redactor.NoticeIfRedacted });
        }

        // ======================= SESSIONS =======================

        private void HandleSessionSave(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<SessionSaveDto>(request);

            if (string.IsNullOrWhiteSpace(body.FilePath))
            {
                WriteError(response, "filePath is required.");
                return;
            }

            // The file is the agent's to read, so it holds what the agent is shown
            var redactor = NewRedactor();
            InvokeOnUI(() =>
            {
                List<HttpRequestEntry> snapshot;
                lock (_vm.CollectionLock)
                {
                    snapshot = _vm.AllRequests.Select(e => AsShownToAgent(e, redactor)).ToList();
                }
                var session = SessionService.BuildSessionData(
                    snapshot,
                    _vm.ExcludedDomainsText,
                    _vm.ExcludedProcessesText,
                    _vm.AlertsVm.Rules.ToList(),
                    _vm.AutoResponderVm.Rules.ToList(),
                    sslPassthroughDomains: _vm.SslPassthroughDomainsText);
                session.Description = body.Description ?? string.Empty;
                SessionService.SaveSession(body.FilePath!, session);
                _vm.StatusMessage = $"Session saved ({snapshot.Count} requests)";
            });

            WriteJson(response, new { success = true, credentialsNotice = redactor.NoticeIfRedacted });
        }

        private void HandleSessionLoad(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<SessionLoadDto>(request);

            if (string.IsNullOrWhiteSpace(body.FilePath))
            {
                WriteError(response, "filePath is required.");
                return;
            }

            if (!File.Exists(body.FilePath))
            {
                WriteError(response, $"Session file not found: {body.FilePath}", 404);
                return;
            }

            // Read the file before touching the grid, so a file that is not a session leaves it as it was.
            SessionData session;
            List<HttpRequestEntry> entries;
            try
            {
                session = SessionService.LoadSession(body.FilePath!);
                entries = SessionService.RestoreRequests(session);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is Newtonsoft.Json.JsonException)
            {
                WriteError(response, $"Not a valid session file: {body.FilePath} ({ex.Message})", 422);
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                WriteError(response, $"Session file cannot be opened: {body.FilePath} ({ex.Message})", 403);
                return;
            }

            int requestCount = InvokeOnUI(() =>
            {
                lock (_vm.CollectionLock) { _vm.AllRequests.Clear(); }
                _vm.TotalRequests = 0;
                _vm.GetCount = 0;
                _vm.PostCount = 0;
                _vm.PutCount = 0;
                _vm.DeleteCount = 0;
                _vm.ErrorCount = 0;
                _vm.TotalDataTransferred = 0;
                _vm.SelectedRequest = null;
                _vm.GraphsVm.Reset();

                lock (_vm.CollectionLock)
                {
                    foreach (var e in entries) _vm.AllRequests.Add(e);
                }
                _vm.ProxyServiceInstance.ContinueRequestIdsAfter(entries.Count > 0 ? entries.Max(e => e.Id) : 0);
                _vm.TotalRequests = entries.Count;
                foreach (var e in entries)
                {
                    switch (e.Method.ToUpperInvariant())
                    {
                        case "GET": _vm.GetCount++; break;
                        case "POST": _vm.PostCount++; break;
                        case "PUT": _vm.PutCount++; break;
                        case "DELETE": _vm.DeleteCount++; break;
                    }
                    if (e.ResponseSize.HasValue) _vm.TotalDataTransferred += e.ResponseSize.Value;
                    if (e.StatusCode >= 400) _vm.ErrorCount++;
                }

                _vm.ExcludedDomainsText = session.Settings.ExcludedDomains;
                _vm.ExcludedProcessesText = session.Settings.ExcludedProcesses;
                _vm.SslPassthroughDomainsText = session.Settings.SslPassthroughDomains ?? string.Empty;
                if (session.Settings.AlertRules != null) _vm.AlertServiceInstance.ReplaceRules(session.Settings.AlertRules);
                _vm.StatusMessage = $"Loaded session: {entries.Count} requests from {session.SavedAt:g}";
                return entries.Count;
            });

            WriteJson(response, new { success = true, requestCount });
        }

        private void HandleSessionRecent(HttpListenerResponse response)
        {
            var recent = SessionService.GetRecentSessions();
            WriteJson(response, recent);
        }

        // ======================= CONTENT =======================

        private void HandleContentDecode(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<ContentDecodeDto>(request);

            if (body.Content == null)
            {
                WriteError(response, "content is required.");
                return;
            }

            var decoded = ContentDecoder.Decode(body.Content, body.Encoding);
            WriteJson(response, new
            {
                decoded = decoded.Decoded,
                encodingApplied = decoded.DecodingApplied,
                wasDecoded = decoded.WasDecoded
            });
        }

        private void HandleContentEncode(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<ContentEncodeDto>(request);

            if (body.Content == null)
            {
                WriteError(response, "content is required.");
                return;
            }

            string encoded;
            string encoding = (body.Encoding ?? "base64").ToLowerInvariant();
            if (encoding == "url")
                encoded = ContentDecoder.UrlEncode(body.Content);
            else
                encoded = ContentDecoder.EncodeBase64(body.Content);

            WriteJson(response, new { encoded });
        }

        // ======================= STATS =======================

        private void HandleGetStats(HttpListenerResponse response)
        {
            var redactor = NewRedactor();
            var data = InvokeOnUI(() =>
            {
                List<HttpRequestEntry> snapshot;
                lock (_vm.CollectionLock)
                {
                    snapshot = _vm.AllRequests.ToList();
                }

                var domainDist = snapshot
                    .GroupBy(r => r.Host)
                    .OrderByDescending(g => g.Count())
                    .Take(20)
                    .Select(g => new { domain = g.Key, count = g.Count() })
                    .ToArray();

                var processDist = snapshot
                    .GroupBy(r => r.ProcessName)
                    .OrderByDescending(g => g.Count())
                    .Take(20)
                    .Select(g => new { process = g.Key, count = g.Count() })
                    .ToArray();

                int slowCount = snapshot.Count(r => r.IsSlow);

                var slowest = snapshot
                    .Where(r => r.Duration.HasValue)
                    .OrderByDescending(r => r.Duration!.Value)
                    .Take(5)
                    .Select(e => MapRequestSummary(e, redactor))
                    .ToArray();

                return new
                {
                    totalRequests = _vm.TotalRequests,
                    getCount = _vm.GetCount,
                    postCount = _vm.PostCount,
                    putCount = _vm.PutCount,
                    deleteCount = _vm.DeleteCount,
                    errorCount = _vm.ErrorCount,
                    totalBytes = _vm.TotalDataTransferred,
                    totalBytesFormatted = _vm.TotalDataFormatted,
                    domainDistribution = domainDist,
                    processDistribution = processDist,
                    slowRequestCount = slowCount,
                    slowestRequests = slowest,
                    credentialsNotice = redactor.NoticeIfRedacted
                };
            });

            WriteJson(response, data);
        }

        // ======================= DOMAINS =======================

        private void HandleGetDomains(HttpListenerResponse response)
        {
            var data = InvokeOnUI(() =>
            {
                List<HttpRequestEntry> snapshot;
                lock (_vm.CollectionLock)
                {
                    snapshot = _vm.AllRequests.ToList();
                }

                var domains = snapshot
                    .Where(r => !string.IsNullOrEmpty(r.Host))
                    .GroupBy(r => r.Host)
                    .OrderByDescending(g => g.Count())
                    .Select(g => new { name = g.Key, count = g.Count() })
                    .ToArray();

                return new { domains };
            });

            WriteJson(response, data);
        }

        // ======================= COMPARISON =======================

        private void HandleCompare(HttpListenerRequest request, HttpListenerResponse response)
        {
            var body = ReadBody<CompareDto>(request);

            var result = InvokeOnUI(() =>
            {
                HttpRequestEntry? entry1, entry2;
                lock (_vm.CollectionLock)
                {
                    entry1 = _vm.AllRequests.FirstOrDefault(r => r.Id == body.RequestId1);
                    entry2 = _vm.AllRequests.FirstOrDefault(r => r.Id == body.RequestId2);
                }
                return (entry1, entry2);
            });

            if (result.entry1 == null || result.entry2 == null)
            {
                WriteError(response, "One or both requests not found.", 404);
                return;
            }

            var e1 = result.entry1;
            var e2 = result.entry2;
            var redactor = NewRedactor();

            // Redact before diffing: a diff of the raw text would show both values side by side
            string reqHeadersDiff = BuildDiff(redactor.Redact(e1.RequestHeaders ?? ""), redactor.Redact(e2.RequestHeaders ?? ""));
            string reqBodyDiff = BuildDiff(e1.RequestBody ?? "", e2.RequestBody ?? "");
            string respHeadersDiff = BuildDiff(redactor.Redact(e1.ResponseHeaders ?? ""), redactor.Redact(e2.ResponseHeaders ?? ""));
            string respBodyDiff = BuildDiff(e1.ResponseBody ?? "", e2.ResponseBody ?? "");

            WriteJson(response, new
            {
                request1 = new
                {
                    id = e1.Id,
                    method = e1.Method,
                    url = redactor.Redact(e1.Url),
                    statusCode = e1.StatusCode,
                    durationMs = e1.Duration?.TotalMilliseconds
                },
                request2 = new
                {
                    id = e2.Id,
                    method = e2.Method,
                    url = redactor.Redact(e2.Url),
                    statusCode = e2.StatusCode,
                    durationMs = e2.Duration?.TotalMilliseconds
                },
                diffs = new
                {
                    requestHeaders = reqHeadersDiff,
                    requestBody = reqBodyDiff,
                    responseHeaders = respHeadersDiff,
                    responseBody = respBodyDiff
                },
                credentialsNotice = redactor.NoticeIfRedacted
            });
        }

        private static string BuildDiff(string text1, string text2)
        {
            var diffBuilder = new InlineDiffBuilder(new Differ());
            var diff = diffBuilder.BuildDiffModel(text1, text2);

            var sb = new StringBuilder();
            foreach (var line in diff.Lines)
            {
                switch (line.Type)
                {
                    case ChangeType.Inserted:
                        sb.AppendLine($"+ {line.Text}");
                        break;
                    case ChangeType.Deleted:
                        sb.AppendLine($"- {line.Text}");
                        break;
                    case ChangeType.Modified:
                        sb.AppendLine($"~ {line.Text}");
                        break;
                    case ChangeType.Unchanged:
                        sb.AppendLine($"  {line.Text}");
                        break;
                    default:
                        sb.AppendLine($"  {line.Text}");
                        break;
                }
            }
            return sb.ToString();
        }

        // ======================= BOOKMARKS =======================

        private void HandleToggleBookmark(string path, HttpListenerRequest request, HttpListenerResponse response)
        {
            // path: /api/bookmarks/{id}/toggle
            string stripped = path.Substring("/api/bookmarks/".Length);
            string idStr = stripped.Substring(0, stripped.IndexOf('/'));
            if (!int.TryParse(idStr, out int id))
            {
                WriteError(response, "Invalid request ID.");
                return;
            }

            BookmarkToggleDto? body = null;
            try { body = ReadBody<BookmarkToggleDto>(request); } catch { }

            var result = InvokeOnUI(() =>
            {
                HttpRequestEntry? entry;
                lock (_vm.CollectionLock)
                {
                    entry = _vm.AllRequests.FirstOrDefault(r => r.Id == id);
                }
                if (entry == null) return (false, false);

                entry.IsBookmarked = !entry.IsBookmarked;
                if (body?.Notes != null)
                    entry.BookmarkNotes = body.Notes;

                return (true, entry.IsBookmarked);
            });

            if (!result.Item1)
            {
                WriteError(response, "Request not found.", 404);
                return;
            }
            WriteJson(response, new { success = true, isBookmarked = result.Item2 });
        }

        private void HandleGetBookmarks(HttpListenerRequest request, HttpListenerResponse response)
        {
            var qs = request.QueryString;
            int skip = int.TryParse(qs["skip"], out var s) ? s : 0;
            int take = int.TryParse(qs["take"], out var t) ? t : 50;
            var redactor = NewRedactor();

            var result = InvokeOnUI(() =>
            {
                List<HttpRequestEntry> snapshot;
                lock (_vm.CollectionLock)
                {
                    snapshot = _vm.AllRequests.Where(r => r.IsBookmarked).ToList();
                }

                int totalCount = snapshot.Count;
                var page = snapshot.Skip(skip).Take(take).ToList();

                return new
                {
                    requests = page.Select(e => MapRequestSummary(e, redactor)).ToArray(),
                    totalCount,
                    credentialsNotice = redactor.NoticeIfRedacted
                };
            });

            WriteJson(response, result);
        }

        // ======================= TLS =======================

        private void HandleGetTls(string path, HttpListenerResponse response)
        {
            // path: /api/requests/{id}/tls
            string stripped = path.Substring("/api/requests/".Length);
            string idStr = stripped.Substring(0, stripped.IndexOf('/'));
            if (!int.TryParse(idStr, out int id))
            {
                WriteError(response, "Invalid request ID.");
                return;
            }

            var entry = InvokeOnUI(() =>
            {
                lock (_vm.CollectionLock)
                {
                    return _vm.AllRequests.FirstOrDefault(r => r.Id == id);
                }
            });

            if (entry == null)
            {
                WriteError(response, "Request not found.", 404);
                return;
            }

            if (entry.TlsInfo == null)
            {
                WriteError(response, "No TLS info available for this request.", 404);
                return;
            }

            var tls = entry.TlsInfo;
            WriteJson(response, new
            {
                subject = tls.Subject,
                issuer = tls.Issuer,
                notBefore = tls.NotBefore.ToString("O"),
                notAfter = tls.NotAfter.ToString("O"),
                serialNumber = tls.SerialNumber,
                thumbprint = tls.Thumbprint,
                signatureAlgorithm = tls.SignatureAlgorithm,
                tlsVersion = tls.TlsVersion,
                cipherSuite = tls.CipherSuite,
                keySize = tls.KeySize,
                chain = tls.Chain.Select(c => new
                {
                    subject = c.Subject,
                    issuer = c.Issuer,
                    thumbprint = c.Thumbprint,
                    notAfter = c.NotAfter.ToString("O")
                }).ToArray(),
                hasErrors = tls.HasErrors,
                errorSummary = tls.ErrorSummary,
                isExpired = tls.IsExpired,
                isNotYetValid = tls.IsNotYetValid,
                daysUntilExpiry = tls.DaysUntilExpiry
            });
        }

        // ======================= WEBSOCKET =======================

        private void HandleGetWebSocket(string path, HttpListenerResponse response)
        {
            // path: /api/requests/{id}/websocket
            string stripped = path.Substring("/api/requests/".Length);
            string idStr = stripped.Substring(0, stripped.IndexOf('/'));
            if (!int.TryParse(idStr, out int id))
            {
                WriteError(response, "Invalid request ID.");
                return;
            }

            var entry = InvokeOnUI(() =>
            {
                lock (_vm.CollectionLock)
                {
                    return _vm.AllRequests.FirstOrDefault(r => r.Id == id);
                }
            });

            if (entry == null)
            {
                WriteError(response, "Request not found.", 404);
                return;
            }

            if (!entry.IsWebSocket || entry.WebSocketMessages == null || entry.WebSocketMessages.Count == 0)
            {
                WriteJson(response, new
                {
                    isWebSocket = entry.IsWebSocket,
                    messages = Array.Empty<object>()
                });
                return;
            }

            var redactor = NewRedactor();
            WriteJson(response, new
            {
                isWebSocket = true,
                messages = entry.WebSocketMessages.Select(m => new
                {
                    timestamp = m.Timestamp.ToString("O"),
                    direction = m.Direction,
                    frameType = m.FrameType,
                    payload = redactor.Redact(m.Payload),
                    payloadLength = m.PayloadLength,
                    parentRequestId = m.ParentRequestId
                }).ToArray(),
                credentialsNotice = redactor.NoticeIfRedacted
            });
        }

        // ======================= HELPERS =======================

        // Exports are only asked for by MCP tools, so they are built from what the agent is shown
        private List<HttpRequestEntry> GetRequestsByIds(List<int>? ids, CredentialRedactor redactor)
        {
            return InvokeOnUI(() =>
            {
                lock (_vm.CollectionLock)
                {
                    var idSet = ids == null || ids.Count == 0 ? null : new HashSet<int>(ids);
                    return _vm.AllRequests
                        .Where(r => idSet == null || idSet.Contains(r.Id))
                        .Select(r => AsShownToAgent(r, redactor))
                        .ToList();
                }
            });
        }

        // Every answer that shows captured traffic goes through one of these, unless the user opted out
        private CredentialRedactor NewRedactor() => new(_vm.RevealCredentialsToAgents);

        // A copy holding the values the agent is shown: what a filter is evaluated on, an export is
        // built from and a session is saved with. The grid's own entry stays as it was captured.
        private static HttpRequestEntry AsShownToAgent(HttpRequestEntry e, CredentialRedactor redactor)
        {
            return new HttpRequestEntry
            {
                Id = e.Id,
                Timestamp = e.Timestamp,
                ResponseTime = e.ResponseTime,
                ProcessName = e.ProcessName,
                ProcessId = e.ProcessId,
                Url = redactor.Redact(e.Url),
                Host = e.Host,
                Scheme = e.Scheme,
                Method = e.Method,
                StatusCode = e.StatusCode,
                ResponseSize = e.ResponseSize,
                Duration = e.Duration,
                IsComplete = e.IsComplete,
                RequestHeaders = redactor.Redact(e.RequestHeaders),
                RequestBody = e.RequestBody,
                RequestContentType = e.RequestContentType,
                ResponseHeaders = redactor.RedactOrNull(e.ResponseHeaders),
                ResponseBody = e.ResponseBody,
                ResponseContentType = e.ResponseContentType,
                DnsLookupMs = e.DnsLookupMs,
                TcpConnectMs = e.TcpConnectMs,
                TlsHandshakeMs = e.TlsHandshakeMs,
                TimeToFirstByteMs = e.TimeToFirstByteMs,
                ContentDownloadMs = e.ContentDownloadMs,
                TlsInfo = e.TlsInfo,
                IsBookmarked = e.IsBookmarked,
                BookmarkNotes = e.BookmarkNotes
            };
        }

        private static object MapRequestSummary(HttpRequestEntry e, CredentialRedactor redactor)
        {
            return new
            {
                id = e.Id,
                timestamp = e.Timestamp.ToString("O"),
                method = e.Method,
                url = redactor.Redact(e.Url),
                host = e.Host,
                scheme = e.Scheme,
                statusCode = e.StatusCode,
                responseSize = e.ResponseSize,
                durationMs = e.Duration?.TotalMilliseconds,
                processName = e.ProcessName,
                isBookmarked = e.IsBookmarked,
                bookmarkNotes = e.BookmarkNotes,
                isSlow = e.IsSlow,
                isWebSocket = e.IsWebSocket,
                isComplete = e.IsComplete
            };
        }

        private static object MapAlertRule(AlertRule r)
        {
            return new
            {
                id = r.Id,
                name = r.Name,
                isEnabled = r.IsEnabled,
                type = r.Type.ToString(),
                pattern = r.Pattern,
                statusCodeMin = r.StatusCodeMin,
                statusCodeMax = r.StatusCodeMax,
                responseTimeThresholdMs = r.ResponseTimeThresholdMs,
                sizeThresholdBytes = r.SizeThresholdBytes,
                playSound = r.PlaySound,
                showToast = r.ShowToast
            };
        }

        private static object MapAutoResponderRule(AutoResponderRule r)
        {
            return new
            {
                id = r.Id,
                isEnabled = r.IsEnabled,
                urlPattern = r.UrlPattern,
                isRegex = r.IsRegex,
                httpMethod = r.HttpMethod,
                responseStatusCode = r.ResponseStatusCode,
                responseHeaders = r.ResponseHeaders,
                responseBody = r.ResponseBody,
                responseFilePath = r.ResponseFilePath,
                delayMs = r.DelayMs,
                matchCount = r.MatchCount
            };
        }

        private static object MapRequestDetail(HttpRequestEntry e, CredentialRedactor redactor)
        {
            return new
            {
                id = e.Id,
                timestamp = e.Timestamp.ToString("O"),
                method = e.Method,
                url = redactor.Redact(e.Url),
                host = e.Host,
                scheme = e.Scheme,
                statusCode = e.StatusCode,
                responseSize = e.ResponseSize,
                durationMs = e.Duration?.TotalMilliseconds,
                processName = e.ProcessName,
                processId = e.ProcessId,
                isBookmarked = e.IsBookmarked,
                bookmarkNotes = e.BookmarkNotes,
                isSlow = e.IsSlow,
                isWebSocket = e.IsWebSocket,
                isComplete = e.IsComplete,
                requestHeaders = redactor.Redact(e.RequestHeaders),
                requestBody = e.RequestBody,
                requestContentType = e.RequestContentType,
                responseHeaders = redactor.RedactOrNull(e.ResponseHeaders),
                responseBody = e.ResponseBody,
                responseContentType = e.ResponseContentType,
                responseTime = e.ResponseTime?.ToString("O"),
                tlsInfo = e.TlsInfo != null ? new
                {
                    subject = e.TlsInfo.Subject,
                    issuer = e.TlsInfo.Issuer,
                    notBefore = e.TlsInfo.NotBefore.ToString("O"),
                    notAfter = e.TlsInfo.NotAfter.ToString("O"),
                    serialNumber = e.TlsInfo.SerialNumber,
                    thumbprint = e.TlsInfo.Thumbprint,
                    signatureAlgorithm = e.TlsInfo.SignatureAlgorithm,
                    tlsVersion = e.TlsInfo.TlsVersion,
                    cipherSuite = e.TlsInfo.CipherSuite,
                    keySize = e.TlsInfo.KeySize,
                    chain = e.TlsInfo.Chain.Select(c => new
                    {
                        subject = c.Subject,
                        issuer = c.Issuer,
                        thumbprint = c.Thumbprint,
                        notAfter = c.NotAfter.ToString("O")
                    }).ToArray(),
                    hasErrors = e.TlsInfo.HasErrors,
                    errorSummary = e.TlsInfo.ErrorSummary,
                    isExpired = e.TlsInfo.IsExpired,
                    daysUntilExpiry = e.TlsInfo.DaysUntilExpiry
                } : null,
                webSocketMessages = e.IsWebSocket && e.WebSocketMessages.Count > 0
                    ? e.WebSocketMessages.Select(m => new
                    {
                        timestamp = m.Timestamp.ToString("O"),
                        direction = m.Direction,
                        frameType = m.FrameType,
                        payload = redactor.Redact(m.Payload),
                        payloadLength = m.PayloadLength
                    }).ToArray()
                    : null,
                timing = new
                {
                    dnsLookupMs = e.DnsLookupMs,
                    tcpConnectMs = e.TcpConnectMs,
                    tlsHandshakeMs = e.TlsHandshakeMs,
                    timeToFirstByteMs = e.TimeToFirstByteMs,
                    contentDownloadMs = e.ContentDownloadMs
                },
                credentialsNotice = redactor.NoticeIfRedacted
            };
        }

        // ======================= DTO CLASSES =======================

        private class FilterRequestBody
        {
            [JsonPropertyName("conditions")]
            public List<FilterConditionDto>? Conditions { get; set; }

            [JsonPropertyName("logicOperator")]
            public string? LogicOperator { get; set; }

            [JsonPropertyName("skip")]
            public int? Skip { get; set; }

            [JsonPropertyName("take")]
            public int? Take { get; set; }
        }

        private class FilterConditionDto
        {
            [JsonPropertyName("field")]
            public string? Field { get; set; }

            [JsonPropertyName("operator")]
            public string? Operator { get; set; }

            [JsonPropertyName("value")]
            public string? Value { get; set; }
        }

        private class FilterPresetDto
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("logicOperator")]
            public string? LogicOperator { get; set; }

            [JsonPropertyName("conditions")]
            public List<FilterConditionDto>? Conditions { get; set; }
        }

        private class ExclusionsDto
        {
            [JsonPropertyName("domains")]
            public List<string>? Domains { get; set; }

            [JsonPropertyName("processes")]
            public List<string>? Processes { get; set; }
        }

        private class SslPassthroughDto
        {
            [JsonPropertyName("domains")]
            public List<string>? Domains { get; set; }
        }

        private class AlertRuleDto
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("isEnabled")]
            public bool? IsEnabled { get; set; }

            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("pattern")]
            public string? Pattern { get; set; }

            [JsonPropertyName("statusCodeMin")]
            public int? StatusCodeMin { get; set; }

            [JsonPropertyName("statusCodeMax")]
            public int? StatusCodeMax { get; set; }

            [JsonPropertyName("responseTimeThresholdMs")]
            public int? ResponseTimeThresholdMs { get; set; }

            [JsonPropertyName("sizeThresholdBytes")]
            public long? SizeThresholdBytes { get; set; }

            [JsonPropertyName("playSound")]
            public bool? PlaySound { get; set; }

            [JsonPropertyName("showToast")]
            public bool? ShowToast { get; set; }
        }

        private class SoundEnabledDto
        {
            [JsonPropertyName("enabled")]
            public bool Enabled { get; set; }
        }

        private class AutoResponderToggleDto
        {
            [JsonPropertyName("enabled")]
            public bool Enabled { get; set; }
        }

        private class AutoResponderRuleDto
        {
            [JsonPropertyName("isEnabled")]
            public bool? IsEnabled { get; set; }

            [JsonPropertyName("urlPattern")]
            public string? UrlPattern { get; set; }

            [JsonPropertyName("isRegex")]
            public bool? IsRegex { get; set; }

            [JsonPropertyName("httpMethod")]
            public string? HttpMethod { get; set; }

            [JsonPropertyName("responseStatusCode")]
            public int? ResponseStatusCode { get; set; }

            [JsonPropertyName("responseHeaders")]
            public string? ResponseHeaders { get; set; }

            [JsonPropertyName("responseBody")]
            public string? ResponseBody { get; set; }

            [JsonPropertyName("responseFilePath")]
            public string? ResponseFilePath { get; set; }

            [JsonPropertyName("delayMs")]
            public int? DelayMs { get; set; }
        }

        private class ReplayRequestDto
        {
            [JsonPropertyName("method")]
            public string? Method { get; set; }

            [JsonPropertyName("url")]
            public string? Url { get; set; }

            [JsonPropertyName("headers")]
            public Dictionary<string, string>? Headers { get; set; }

            [JsonPropertyName("body")]
            public string? Body { get; set; }
        }

        private class ExportIdsDto
        {
            [JsonPropertyName("requestIds")]
            public List<int>? RequestIds { get; set; }
        }

        private class PostmanExportDto
        {
            [JsonPropertyName("requestIds")]
            public List<int>? RequestIds { get; set; }

            [JsonPropertyName("collectionName")]
            public string? CollectionName { get; set; }
        }

        private class SessionSaveDto
        {
            [JsonPropertyName("filePath")]
            public string? FilePath { get; set; }

            [JsonPropertyName("description")]
            public string? Description { get; set; }
        }

        private class SessionLoadDto
        {
            [JsonPropertyName("filePath")]
            public string? FilePath { get; set; }
        }

        private class ContentDecodeDto
        {
            [JsonPropertyName("content")]
            public string? Content { get; set; }

            [JsonPropertyName("encoding")]
            public string? Encoding { get; set; }
        }

        private class ContentEncodeDto
        {
            [JsonPropertyName("content")]
            public string? Content { get; set; }

            [JsonPropertyName("encoding")]
            public string? Encoding { get; set; }
        }

        private class CompareDto
        {
            [JsonPropertyName("requestId1")]
            public int RequestId1 { get; set; }

            [JsonPropertyName("requestId2")]
            public int RequestId2 { get; set; }
        }

        private class BookmarkToggleDto
        {
            [JsonPropertyName("notes")]
            public string? Notes { get; set; }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
