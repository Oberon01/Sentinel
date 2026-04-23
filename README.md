# Sentinel — System Monitor Service

A lightweight, always-on Windows Service built on .NET 8 that monitors system resources, network connectivity, file system activity, and Windows Event Logs. Delivers real-time Windows toast notifications on threshold breaches and writes structured rolling log files.

---

## Table of Contents

- [Requirements](#requirements)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Service Management](#service-management)
- [Log Files](#log-files)
- [Troubleshooting](#troubleshooting)

---

## Requirements

- Windows 10 or Windows 11 (x64)
- [.NET 8 Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- PowerShell 5.1 or later
- Admin privileges for service installation

---

## Quick Start

### 1. Clone and Build

```powershell
git clone https://github.com/yourusername/Sentinel.git
cd Sentinel
```

### 2. Publish

```powershell
cd Sentinel.Service
dotnet publish -c Release -r win-x64 --self-contained false -o C:\Services\Sentinel
```

### 3. Create Log Directory

```powershell
New-Item -ItemType Directory -Force -Path "C:\Logs\Sentinel"
```

### 4. Configure

Edit `C:\Services\Sentinel\appsettings.json` before installing. At minimum, add any directories you want to watch and verify your ping targets. See [Configuration](#configuration) below.

### 5. Install the Service

Run the following in an **elevated (Admin) PowerShell**:

```powershell
sc.exe create "SentinelMonitor" binPath="C:\Services\Sentinel\Sentinel.Service.exe" start=auto DisplayName="Sentinel Monitor"
sc.exe failure "SentinelMonitor" reset=60 actions=restart/2000/restart/2000/restart/2000
sc.exe failureflag "SentinelMonitor" 1
sc.exe start "SentinelMonitor"
```

### 6. Verify

```powershell
sc.exe query "SentinelMonitor"
# Expected: STATE: 4 RUNNING

Get-Content "C:\Logs\Sentinel\sentinel-$(Get-Date -Format 'yyyyMMdd').log" -Wait
# Expected: Live poll output every 30 seconds
```

---

## Configuration

All settings live in `C:\Services\Sentinel\appsettings.json`.

> **Important:** Always edit the file in the **publish directory** (`C:\Services\Sentinel\`), not the development folder. Changes apply automatically within ~4 seconds — no restart required.

> **Validate before saving:** `Get-Content C:\Services\Sentinel\appsettings.json | ConvertFrom-Json`

### Full Configuration Reference

```json
{
  "Serilog": {
    "MinimumLevel": "Information"
  },
  "Sentinel": {
    "PollIntervalSeconds": 30,
    "LogPath": "C:\\Logs\\Sentinel",
    "Resources": {
      "CpuPercentThreshold": 85.0,
      "RamPercentThreshold": 97.0,
      "DiskFreePercentThreshold": 10.0,
      "MonitoredDrives": [ "C:\\" ]
    },
    "Network": {
      "PingTargets": [ "8.8.8.8", "1.1.1.1" ],
      "LatencyThresholdMs": 200
    },
    "FileSystem": {
      "WatchPaths": [],
      "IncludeSubdirectories": true
    },
    "EventLog": {
      "LogNames": [ "System", "Application" ]
    }
  }
}
```

### Settings

| Setting | Default | Description |
|---|---|---|
| `PollIntervalSeconds` | `30` | How often all monitors run |
| `LogPath` | `C:\Logs\Sentinel` | Directory for log files |
| `CpuPercentThreshold` | `85.0` | CPU alert threshold (%) |
| `RamPercentThreshold` | `97.0` | RAM alert threshold (%) |
| `DiskFreePercentThreshold` | `10.0` | Minimum free disk space before alert (%) |
| `MonitoredDrives` | `["C:\\"]` | Drives to check for disk space |
| `PingTargets` | `["8.8.8.8","1.1.1.1"]` | Hosts to ping for connectivity checks |
| `LatencyThresholdMs` | `200` | Round-trip latency alert threshold (ms) |
| `WatchPaths` | `[]` | Directories to watch for file changes |
| `IncludeSubdirectories` | `true` | Whether to monitor subdirectories |
| `LogNames` | `["System","Application"]` | Windows Event Logs to monitor |

### Common Config Changes

**Add a watch directory:**
```json
"WatchPaths": [ "C:\\Users\\YourName\\Documents", "C:\\SomeOtherFolder" ]
```

**Add an internal server to ping:**
```json
"PingTargets": [ "8.8.8.8", "1.1.1.1", "192.168.1.1" ]
```

**Monitor additional drives:**
```json
"MonitoredDrives": [ "C:\\", "D:\\" ]
```

**Reduce alert sensitivity:**
```json
"CpuPercentThreshold": 95.0,
"RamPercentThreshold": 98.0
```

---

## Service Management

### Start / Stop / Status

```powershell
sc.exe start "SentinelMonitor"
sc.exe stop "SentinelMonitor"
sc.exe query "SentinelMonitor"
```

### Deploy an Update

```powershell
cd Sentinel.Service
dotnet publish -c Release -r win-x64 --self-contained false -o C:\Services\Sentinel
sc.exe stop "SentinelMonitor"
sc.exe start "SentinelMonitor"
```

> The publish command overwrites binaries only. `appsettings.json` in `C:\Services\Sentinel\` is preserved.

### Uninstall

```powershell
sc.exe stop "SentinelMonitor"
sc.exe delete "SentinelMonitor"
```

---

## Log Files

Logs are written to `C:\Logs\Sentinel\` as daily rolling files:

```
C:\Logs\Sentinel\sentinel-20260423.log
```

Files are retained for 30 days and then automatically deleted.

### Tail the Log Live

```powershell
Get-Content "C:\Logs\Sentinel\sentinel-$(Get-Date -Format 'yyyyMMdd').log" -Wait
```

### Log Levels

| Level | Meaning |
|---|---|
| `INF` | Normal healthy poll result |
| `WRN` | Threshold breached or alert fired |
| `ERR` | Monitor exception or notification failure |

---

## Troubleshooting

### Service won't start (error 1053 — timeout)

Run the exe directly to see the actual error:

```powershell
cd C:\Services\Sentinel
.\Sentinel.Service.exe
```

Common causes:
- .NET 8 Runtime not installed
- Invalid `appsettings.json` — look for a JSON parse exception in the output

### Config changes not reloading

You're likely editing the development `appsettings.json`. Always edit the one in `C:\Services\Sentinel\`.

### File system changes not logging

`WatchPaths` is empty by default. Add paths to `C:\Services\Sentinel\appsettings.json` and save — the service will reload automatically.

### No toast notifications

Toast notifications only appear when a user is logged in. The service continues monitoring and logging regardless. Check the log file for `UNHEALTHY` entries to confirm alerts are firing.

### Verify SCM restart is configured

```powershell
sc.exe qfailure "SentinelMonitor"
sc.exe qfailureflag "SentinelMonitor"
# FAILURE_ACTIONS_ON_NONCRASH_FAILURES should be TRUE
```

If not set, re-run:
```powershell
sc.exe failure "SentinelMonitor" reset=60 actions=restart/2000/restart/2000/restart/2000
sc.exe failureflag "SentinelMonitor" 1
```

---

## Project Structure

```
Sentinel/
├── Sentinel.sln
├── Sentinel.Core/               # Shared contracts and models
│   ├── IMonitor.cs
│   ├── MonitorResult.cs
│   ├── SentinelOptions.cs
│   └── AlertService.cs
├── Sentinel.Monitors/           # Monitor implementations
│   ├── SystemResourceMonitor.cs
│   ├── NetworkMonitor.cs
│   ├── FileSystemMonitor.cs
│   └── EventLogMonitor.cs
└── Sentinel.Service/            # Windows Service host
    ├── Program.cs
    ├── Worker.cs
    ├── ConfigWatcher.cs
    └── appsettings.json
```

---

## Built With

- [.NET 8](https://dotnet.microsoft.com/) — Worker Service + Windows Service hosting
- [Serilog](https://serilog.net/) — Structured logging with rolling file sink
- `System.Diagnostics.PerformanceCounter` — CPU metrics
- `kernel32.dll GlobalMemoryStatusEx` via P/Invoke — accurate RAM metrics
- `System.IO.FileSystemWatcher` — file system monitoring
- `System.Net.NetworkInformation.Ping` — network connectivity checks
- PowerShell subprocess — Windows toast notification delivery
