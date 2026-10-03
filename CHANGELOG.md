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

[Unreleased]: https://github.com/Makhsum/httptrafficmonitr/commits/main
