using Sentinel.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Sentinel.Monitors;

public sealed class SystemResourceMonitor : IMonitor
{
    public string Name => "SystemResources";

    private readonly SentinelOptions _options;
    private readonly ILogger<SystemResourceMonitor> _logger;
    private PerformanceCounter? _cpuCounter;
    private bool _primed = false;

    public SystemResourceMonitor(IOptions<SentinelOptions> options, ILogger<SystemResourceMonitor> logger)
    {
        _options = options.Value;
        _logger = logger;
        
    }

    public async Task<MonitorResult> CheckAsync(CancellationToken cancellationToken = default)
    {

        if (_cpuCounter is null)
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // First call always returns 0 — prime it
            _primed = false;
        }

        if (!_primed)
        {
            await Task.Delay(500, cancellationToken); // Let CPU counter settle
            _primed = true;
        }
        
        var cpu = _cpuCounter.NextValue();
        var ram = GetRamUsagePercent();
        var drives = GetDiskMetrics();

        var metrics = new Dictionary<string, object>
        {
            ["cpu_percent"] = Math.Round(cpu, 1),
            ["ram_percent"] = Math.Round(ram, 1)
        };

        foreach (var (drive, freePercent) in drives)
            metrics[$"disk_free_percent_{drive}"] = Math.Round(freePercent, 1);

        var issues = new List<string>();

        if (cpu > _options.Resources.CpuPercentThreshold)
            issues.Add($"CPU at {cpu:F1}%");

        if (ram > _options.Resources.RamPercentThreshold)
            issues.Add($"RAM at {ram:F1}%");

        foreach (var (drive, freePercent) in drives)
        {
            if (freePercent < _options.Resources.DiskFreePercentThreshold)
                issues.Add($"Disk {drive} only {freePercent:F1}% free");
        }

        if (issues.Count > 0)
        {
            var summary = string.Join(", ", issues);
            return MonitorResult.Unhealthy(Name, summary, metrics);
        }

        return MonitorResult.Healthy(Name, $"CPU {cpu:F1}% | RAM {ram:F1}%", metrics);
    }

    private static double GetRamUsagePercent()
    {
        var memStatus = new MEMORYSTATUSEX();
        GlobalMemoryStatusEx(memStatus);
        return memStatus.dwMemoryLoad;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(MEMORYSTATUSEX lpBuffer);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength = 64;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private Dictionary<string, double> GetDiskMetrics()
    {
        var result = new Dictionary<string, double>();

        foreach (var drivePath in _options.Resources.MonitoredDrives)
        {
            try
            {
                var info = new DriveInfo(drivePath);
                if (info.IsReady)
                {
                    var freePercent = (double)info.AvailableFreeSpace / info.TotalSize * 100.0;
                    result[drivePath.Replace("\\", "")] = freePercent;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read disk info for {Drive}", drivePath);
            }
        }

        return result;
    }
}