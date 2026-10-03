# Changelog

All notable changes to HTTP Traffic Monitor are listed here, newest first.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Each released version gets its own section with its release date and a link to its [GitHub release](https://github.com/Makhsum/httptrafficmonitr/releases).

## Released versions

None yet — no version has been tagged so far. Until the first release, build the app from source as described in [CONTRIBUTING.md](CONTRIBUTING.md#build-and-run-from-a-fresh-clone); everything on `main` is listed under **Unreleased** below.

## [Unreleased]

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
- A CSV export writes a quote inside a URL, host or process name twice, so a URL such as `?q="a,b"` no longer splits its row into an extra column.

[Unreleased]: https://github.com/Makhsum/httptrafficmonitr/commits/main
