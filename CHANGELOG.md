# Changelog

All notable changes to HTTP Traffic Monitor are listed here, newest first.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Each released version gets its own section with its release date and a link to its [GitHub release](https://github.com/Makhsum/httptrafficmonitr/releases).

## Released versions

- [0.1.0](https://github.com/Makhsum/httptrafficmonitr/releases/tag/v0.1.0) — 2026-10-04, the first release.

## [Unreleased]

### Security

- MCP tool answers hide the values of Authorization, Proxy-Authorization, Cookie and Set-Cookie headers and of common token query parameters by default. The toolbar switch "Agent sees credentials" shows them again; only the user can turn it on.
- MCP tool answers also hide the values of common API-key headers (such as `X-Api-Key`, `X-Auth-Token`, `Api-Key`) and of password and token fields in form-encoded and JSON bodies (such as `password`, `access_token`, `refresh_token`, `id_token`, and any field whose name holds "password", like `new_password`, `user[password]` or `confirmPassword`), in request details, comparisons, WebSocket messages and replay answers. The name stays readable, the rest of the body is unchanged, and the switch "Agent sees credentials" shows them again.
- Exports made through the MCP tools (curl, HAR, Postman, JSON, CSV) and sessions saved through `save_session` hide the same values and follow the same switch. Exports and sessions made in the app window stay complete.
- MCP tool answers, agent exports and sessions saved through `save_session` also hide tokens a service carries in the URL path: a Telegram bot token (`https://api.telegram.org/bot[redacted]/getUpdates`) and the secret part of Slack and Discord webhook URLs. Host and endpoint stay readable, and the switch "Agent sees credentials" shows the complete URL again.
- They also hide a token parameter carried URL-encoded inside another query value, as a redirect target holds it (`/redirect-to?url=http%3A%2F%2Fexample.com%2Fcb%3Faccess_token%3D[redacted]`, also a `next=` or `return_to=` target encoded twice, a value that starts with the token such as `?state=access_token%3D[redacted]`, a token after `#` in a redirect target or a `Location` header, and a Telegram bot token or Slack or Discord webhook URL encoded inside another query value). The rest of the target stays readable, and the switch "Agent sees credentials" shows the complete URL again.
- They also hide the value of a password or token field sent as its own part of a `multipart/form-data` post (a login or upload form, also as a .NET `HttpClient` writes it with the field name unquoted), keeping the field name and leaving the other fields and attached files unchanged, and the value of a `Cookie` or `Set-Cookie` header a server echoes back inside a JSON body (`"Cookie": "session=[redacted]; theme=[redacted]"`, also the `HTTP_COOKIE` entry a CGI or PHP server lists), in tool answers, comparisons, replay answers and agent exports. Cookie names, `Path`, `Expires` and the other attributes stay readable, and the switch "Agent sees credentials" shows the values again.
- They also hide the values of the cookies an echo service or debug page lists already parsed as a JSON object, one property per cookie (`"cookies": {"session": "[redacted]", "theme": "[redacted]"}`, as httpbin's `/cookies` answers, also Express's `"signedCookies"` and PHP's `"_COOKIE"`, also named `"$_COOKIE"`), and the `value` of each entry of a cookie list a HAR-style echo service such as mockbin answers (`"cookies": [{"name": "session", "value": "[redacted]", "path": "/"}]`, also when an entry holds an object of its own such as the `partitionKey` Chrome DevTools lists), in tool answers, agent exports and sessions saved through `save_session`. Cookie names and the other properties of the body stay readable, and the switch "Agent sees credentials" shows the values again.
- The MCP server is read-only by default: `replay_request` only sends GET, and the tools that change the Auto-Responder (`toggle_auto_responder`, `add_auto_responder_rule`, `update_auto_responder_rule`, `delete_auto_responder_rule`) are not offered. The toolbar switch "Agent may make changes" allows both; only the user can turn it on. Auto-Responder rules set up in the app keep answering live traffic either way.
- The switch "Agent may make changes" now also covers the other tools that change the user's capture: `clear_requests`, `start_proxy`, `stop_proxy`, `load_session`, `manage_exclusions`, `add_alert_rule` and `delete_alert_rule` are not offered by default, and the app refuses these changes from the local API with 403 until the switch is on. The new read tool `get_exclusions` lists the exclusions by default. Clearing, starting, stopping, loading a session and changing exclusions or alert rules in the app window work either way.
- `pause_capture` and `resume_capture` are now behind the same switch: by default an agent is not offered them and the app refuses pausing and resuming the capture from the local API with 403, so an agent can no longer leave the capture paused without the user asking. Pausing and resuming in the app window work either way.
- `save_session` no longer replaces an existing file while "Agent may make changes" is off: the app refuses a path that already exists with 403 and tells the agent to choose a new file name, so an agent can no longer overwrite a document or the user's own complete session with a redacted copy. Saving to a new file works by default; with the switch on, saving over an existing session works as before. Saving a session in the app window is not affected.
- Web pages open in a browser can no longer use the app's local API on `localhost:18081`: it no longer sends `Access-Control-Allow-Origin: *`, and it refuses with 403 every request a browser marks as coming from a page (an `Origin` header, or `Sec-Fetch-Site` other than `none`), whether "Agent may make changes" is on or off. So a page can neither read captured traffic nor replay requests or change Auto-Responder rules. The MCP server and other programs on the computer are not affected.
- The toolbar box "Agent replay hosts" limits which hosts `replay_request` may reach: once it lists hosts (`*.example.com` for subdomains), a replay to any other host is refused with a message naming that host. Empty, the default, keeps replay open to any host. No MCP tool can change the list, and the Replay window in the app is not limited by it.

### Fixed

- A curl command exported for a URL holding `[ ]` or `{ }` — a hidden `bot[redacted]` or `token=[redacted]`, or a query like `?ids[]=1` — now carries `-g`, so curl sends it instead of stopping with "bad range in URL".

## [0.1.0] - 2026-10-04

### Added

- System-wide HTTP/HTTPS capture as the Windows proxy, with a locally generated root CA for HTTPS and the original proxy settings restored on Stop.
- Live request grid with method, URL, status, size, duration and the process that sent each request; quick search and Process, Domain, Method and Status filters.
- Request details in Request, Response, Timing, Certificate, Decoded and Exclusions tabs.
- Advanced filters with AND/OR conditions, saved as named presets.
- Replay and compose requests, with the original and the new response side by side.
- Compare two requests line by line.
- Alert rules for status codes, response times and patterns, with a live event feed.
- Auto-Responder: answer matching requests with a canned response or a file, with an optional delay.
- Live charts for requests per second, bandwidth, top domains and top processes.
- Save and load capture sessions, bookmark requests, export as CSV, HAR, JSON, cURL or Postman collection.
- Light and dark themes.
- MCP server (`HttpTrafficMonitor.McpServer`) with 42 tools, so AI assistants can drive the running app over its local API; a project-scoped `.mcp.json` starts it in Claude Code.
- Contribution guide, code of conduct, security policy, issue forms for bug reports and feature requests, and this changelog.

### Removed

- An unrelated Telegram-bot deploy workflow and its leftovers in the solution.

### Fixed

- MCP server: `get_requests` and `filter_requests` show each request's duration and process instead of `(?) [?]`, and `get_request_details` shows them too.
- MCP server: `get_traffic_stats` includes the data transferred, the GET/POST/PUT/DELETE counts and the slowest requests, the same figures as the app's status bar.
- MCP server: `compare_requests` writes both durations the way the app does (`177 ms`, `-` while pending) instead of a raw millisecond number.
- MCP server: WebSocket messages show their frame type, size and payload, and TLS details show the protocol when the app has it.
- MCP server: `get_bookmarks` lists every request bookmarked in the app, with its notes, instead of `No bookmarked requests found.`; `toggle_bookmark` says whether the request is now bookmarked.
- MCP server: `compare_requests` shows the changed lines of the request and response headers and bodies, the same diff as the app's Compare view, and its timing line (`A | B | Δ`).
- The bookmark star in the request grid bookmarks the row it is clicked on; before, it toggled the row that was selected until then.
- MCP server: `get_alert_events` lists the alert events the app has recorded, with time, rule and message, instead of `No alert events found.`
- MCP server: `export_as_csv` and `export_as_curl` return the CSV text and the curl command themselves instead of the app's escaped JSON reply; `export_as_curl` says when a request does not exist.
- MCP server: `get_request_tls_info` says when a request was sent over plain HTTP or has no TLS details, instead of asking whether the app is running.
- An exported or copied cURL command no longer carries the captured `Content-Length`, which cut off the formatted request body when the command was run.
- An exported or copied cURL command runs as pasted: quotes in the URL and header values are escaped, a HEAD request uses `-I` instead of waiting for a body, and `--compressed` is added when the request accepted a compressed response.
- The Timing tab shows the measured DNS lookup, TCP connect, TLS handshake, time to first byte and download of each request, with bars sized by their share; before, every phase read `- ms`. A phase that was not measured reads `n/a`.
- A request's duration runs until its response body has been received, so a slow download no longer shows a shorter duration (and Timing tab total) than its own download phase. MCP server: `get_request_details` shows the timing breakdown in whole milliseconds.
- A CSV export writes a quote inside a URL, host or process name twice, so a URL such as `?q="a,b"` no longer splits its row into an extra column.
- Auto-Responder: a matched rule answers with the status code it is set to (for example `503` or `404`) instead of always `200`, and a mocked request shows its status, size and duration in the grid and its response in the detail tabs.
- MCP server: `add_auto_responder_rule` and `update_auto_responder_rule` show the rule's URL pattern, method and status code in their confirmation.
- The Top Domains and Top Processes charts list each name and count in a legend beside the pie, in a color that follows the theme; before, the labels sat on the slices, piled up once several small slices were side by side, and turned white on the light theme's near-white card.
- The Compare window shows every line of a diff on the dark theme; before, the unchanged lines were white text on a white list, so only the changed lines could be made out.
- MCP server: `load_session` reports how many requests the app restored, the same number as the grid and status bar; before, it always said `Requests loaded: 0`.
- A HAR export lists each request's DNS lookup, connect, TLS handshake, wait and download times as the Timing tab shows them; before, the whole duration was written as server wait. A phase that was not measured is written as `-1`, the HAR format's "not available".
- A HAR export carries every field HAR 1.2 requires (`cookies`, `redirectURL`, no empty `postData`), so stricter HAR viewers such as the perf-cascade waterfall open it instead of failing.
- MCP server: `add_alert_rule` shows the new rule's name, type, enabled state, pattern and thresholds in its confirmation, as the app stored them; before, only the ID line was filled in.
- MCP server: `add_alert_rule` with a type the app does not know, such as `Latency`, creates no rule and names the supported types; before, it created a `StatusCode` rule and reported success. A type in any letter case, such as `responsetime`, is still accepted.
- MCP server: `add_alert_rule` refuses a rule that could never fire and says what is wrong, such as a status code range of `599`–`500`, a `ResponseTime` rule without `responseTimeThresholdMs`, a `RequestSize` or `ResponseSize` rule without `sizeThresholdBytes`, a `Domain` or `Process` rule without a pattern, a pattern like `*.httpbin.org`, which is matched as plain text and so never as a wildcard, a status code range outside `100`–`999` such as `1000`–`2000`, a pattern with a leading or trailing space, a `Domain` pattern with a scheme or path such as `http://example.com`, or a `Process` pattern ending in `.exe`; before, it reported success and the rule stayed silent. A rule without a name is refused too, instead of showing as a blank row in the Alerts tab, and a `StatusCode` rule without a range shows the range it fires on, `400`–`599`, in its confirmation and in `get_alert_rules`.
- A captured request lists its `Host` header once and its request line ends in `HTTP/1.1`, as the client sent it; before, `Host` appeared twice and the request line read `POST /posts 1.1`. This shows in the Request tab, the Compare window's Req Headers tab and MCP `get_request_details`.
- curl, HAR and Postman exports no longer list a request line as a header: a `PATCH`, `OPTIONS` or `HEAD` request whose URL holds a colon, such as `?since=10:00`, was exported with a header named after its own request line.
- A saved session keeps each request's DNS lookup, TCP connect, TLS handshake, time to first byte and download times and the time its response was received, so after loading it the Timing tab and MCP `get_request_details` show the same breakdown as before saving; before, only the duration survived and the Timing tab read `Received: -`. Sessions saved by earlier versions still load, their requests without a breakdown.
- A saved session keeps each HTTPS request's server certificate, its chain and its certificate errors, so after loading it the Certificate tab and MCP `get_request_tls_info` show them as before saving; before, every loaded request read `No TLS certificate information available for this request.` Sessions saved by earlier versions still load, their requests without a certificate.
- Loading a saved session brings back the alert rules it was saved with, so the Alerts tab and MCP `get_alert_rules` list them with their type and thresholds again and they fire on new traffic; before, only the requests, exclusions and SSL passthrough domains came back and the rules in place stayed. Sessions saved by earlier versions hold their alert rules too and bring them back the same way.
- The Alerts tab refuses a rule that could never fire and says below the Add button what is wrong and what to use instead: a `Domain` or `Process` rule without a pattern, a pattern like `*.example.com`, which is matched as plain text and so never as a wildcard, a pattern with a leading or trailing space, a `Domain` pattern with a scheme or path such as `http://example.com/api`, or a `Process` pattern ending in `.exe`; before, the rule was added without a word and stayed silent on the traffic it was meant to catch. It is the same check MCP `add_alert_rule` applies. A `StatusCode`, `ResponseTime`, `RequestSize` or `ResponseSize` rule with a pattern is refused as well, because these rules do not read the pattern: one named for `404` with the pattern `404` watched status codes `500`–`599` and stayed silent on a 404; the message names what such a rule watches.

[Unreleased]: https://github.com/Makhsum/httptrafficmonitr/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Makhsum/httptrafficmonitr/releases/tag/v0.1.0
