# Contributing to HTTP Traffic Monitor

Thanks for wanting to help. Bug reports, ideas and pull requests are all welcome — this guide shows how to get from a fresh clone to a running app and MCP server, and what a change needs before it can be merged.

By taking part you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Reporting a bug or suggesting a feature

Open a [new issue](https://github.com/Makhsum/httptrafficmonitr/issues/new/choose) and pick a form:

- **Bug report** — something does not work as described. Include your Windows version and the app version, the steps that lead to the problem and what you expected instead.
- **Feature request** — something the app or the MCP server should do but does not.

Please search the [existing issues](https://github.com/Makhsum/httptrafficmonitr/issues) first; a 👍 on an existing one helps more than a duplicate.

**Found a security problem?** Do not open an issue — follow [SECURITY.md](SECURITY.md) and report it privately.

## Build and run from a fresh clone

### What you need

- Windows 10 or 11 — the app is WPF and only runs on Windows
- [.NET 8 SDK](https://dotnet.microsoft.com/download) — check with `dotnet --list-sdks`
- Git
- Administrator rights — the app registers itself as the system proxy and installs a local root CA
- Optional: Visual Studio 2022 or JetBrains Rider

### 1. Clone and build

```
git clone https://github.com/Makhsum/httptrafficmonitr.git
cd httptrafficmonitr
dotnet build HttpTrafficMonitor.sln
```

This restores the NuGet packages and builds both projects:

| Project | Output |
|---|---|
| `HttpTrafficMonitor.csproj` — the desktop app | `bin/Debug/net8.0-windows/HttpTrafficMonitor.exe` |
| `HttpTrafficMonitor.McpServer/HttpTrafficMonitor.McpServer.csproj` — the MCP server | `HttpTrafficMonitor.McpServer/bin/Debug/net8.0/HttpTrafficMonitor.McpServer.exe` |

### 2. Run the app

```
bin\Debug\net8.0-windows\HttpTrafficMonitor.exe
```

Windows asks for administrator rights (the app's `app.manifest` requires them), then the app shows a security notice. Click **Start** to capture: the app installs its root CA and becomes the system proxy on port `18080`; **Stop** or closing the app restores your previous proxy settings. To debug, open `HttpTrafficMonitor.sln` in Visual Studio started as administrator and press F5.

While it runs, the app also serves its local API on `http://localhost:18081/api/` — that is what the MCP server talks to.

### 3. Run the MCP server

The MCP server is a separate console process that speaks MCP over stdio and forwards each tool call to the app's local API, so start the app first.

- **In Claude Code** — open the clone: the project-scoped `.mcp.json` starts the server with `dotnet run --project HttpTrafficMonitor.McpServer`. Approve the `httptrafficmonitor` server when asked.
- **In any other MCP client** — point it at `HttpTrafficMonitor.McpServer/bin/Debug/net8.0/HttpTrafficMonitor.McpServer.exe`; the README's [Configuring the MCP server](README.md#configuring-the-mcp-server) has ready-made config blocks.
- **By hand** — `dotnet run --project HttpTrafficMonitor.McpServer` starts it and waits for MCP messages on stdin; stop it with Ctrl+C.

If the app's API is not on its default port, set `HTM_API_URL` (for example `http://localhost:18081`) for the MCP server. A tool that answers "Make sure HttpTrafficMonitor is running" means the app is not running or not reachable on that address.

## Where things live

```
Models/          Request/session/alert/auto-responder/TLS data models
Services/        Proxy, alerts, export, replay, session, content decoding, IPC API, theming
ViewModels/      MVVM view models per view/panel
Views/           MainWindow + dialogs (Advanced Filter, Replay/Compose, Comparison)
HttpTrafficMonitor.McpServer/   Standalone MCP server (stdio) bridging to the running app's IPC API
```

- **A new MCP tool** goes into the matching class in `HttpTrafficMonitor.McpServer/Tools/` as a `[McpServerTool]` method with a `Description`, and calls the app through `IpcClient`. If the app has no endpoint for it yet, add the route in `Services/IpcApiService.cs`.
- **A new control in the UI** gets an `AutomationProperties.AutomationId` in the `Area.NameKind` style the existing ones use (`Toolbar.StartButton`, `FilterBar.SearchTextBox`), so UI tests can find it.

## Making a change

1. Fork the repository and create a branch from `main`.
2. Keep the change to one thing — one fix or one feature per pull request. For anything larger than a small fix, open an issue first so we can agree on the approach before you spend the time.
3. Write code that looks like the code around it: the same structure, naming and error handling as the neighbouring files, nullable reference types respected, no new warnings.
4. Build with `dotnet build HttpTrafficMonitor.sln` — it must finish with 0 errors.
5. There is no automated test suite yet, so check your change by hand in the running app (and through an MCP client if you touched the MCP server). Say in the pull request what you tried.
6. Update `README.md` when you change what a user sees, and add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md).
7. Write commit messages that say what changed and why, in the imperative ("Show the process name in the request grid"), and open a pull request against `main`.

## License

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE) of this project.
