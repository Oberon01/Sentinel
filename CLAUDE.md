# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```powershell
# Build
dotnet build

# Run locally (console output)
dotnet run --project Sentinel.Service

# Publish as self-contained Windows Service
cd Sentinel.Service
dotnet publish -c Release -r win-x64 --self-contained false -o C:\Services\Sentinel
```

There are no test projects in this solution.

## Architecture

Sentinel is a Windows background service that polls health monitors and emits Windows toast alerts. It has three projects:

- **Sentinel.Core** — shared contracts: `IMonitor`, `MonitorResult`, `AlertService`, `SentinelOptions`
- **Sentinel.Monitors** — five concrete `IMonitor` implementations (system resources, network ping, filesystem, event log, SignalDaemon)
- **Sentinel.Service** — Windows Service host: DI wiring (`Program.cs`), polling loop (`Worker.cs`), config hot-reload (`ConfigWatcher.cs`)

### Monitor pattern

Every monitor implements `IMonitor`:

```csharp
Task<MonitorResult> CheckAsync(CancellationToken ct);
string Name { get; }
```

`MonitorResult` is a record with static factories — `MonitorResult.Healthy(summary, metrics)` and `MonitorResult.Unhealthy(summary, metrics)`. `Worker` polls all registered monitors on a configurable interval and calls `AlertService` on unhealthy results.

To add a new monitor: implement `IMonitor` in `Sentinel.Monitors`, register it as a singleton in `Program.cs`.

### Configuration

`SentinelOptions` (bound from `appsettings.json`) is the root options class. All monitors receive their sub-options via `IOptions<SentinelOptions>`. `ConfigWatcher` detects changes to `appsettings.json` and calls `Environment.Exit(1)` — the Windows SCM restarts the service, which picks up the new config.

### SignalDaemon subsystem

`SignalDaemonMonitor` is the most complex monitor. It lives in `Sentinel.Monitors/SignalDaemon/` and uses:
- `NetworkConnectionScanner` — P/Invoke into `iphlpapi.dll` to enumerate all active TCP connections
- `BlocklistService` — SQLite-backed domain/IP blocklist with 10-minute DNS cache
- `DetectionRepository` — SQLite persistence for matched connections

This monitor is disabled by default (`"Enabled": false` in config).

### Alerting

`AlertService` (singleton) fires Windows toast notifications via a PowerShell subprocess. It enforces a per-title 5-minute cooldown to suppress repeated alerts.

### Logging

Serilog is configured in `Program.cs` with console + rolling file sinks. Logs rotate daily with 30-day retention. File path: `C:\Services\Sentinel\logs\sentinel-.log`.

## Key dependencies

| Package | Purpose |
|---|---|
| `Microsoft.Data.Sqlite` | SignalDaemon blocklist + detection DB |
| `System.Diagnostics.PerformanceCounter` | CPU/disk metrics |
| `Microsoft.Toolkit.Uwp.Notifications` | Toast notifications |
| `Serilog.*` | Structured logging |
| `Microsoft.Extensions.Hosting.WindowsServices` | SCM integration |

P/Invoke is used in two places: `kernel32.dll` (`GlobalMemoryStatusEx`) for accurate RAM stats, and `iphlpapi.dll` (`GetExtendedTcpTable`) for TCP connection enumeration.
