namespace Sentinel.Core;

public sealed class SentinelOptions
{
    public ResourceThresholds Resources { get; init; } = new();
    public NetworkOptions Network { get; init; } = new();
    public FileSystemOptions FileSystem { get; init; } = new();
    public EventLogOptions EventLog { get; init; } = new();
    public int PollIntervalSeconds { get; init; } = 30;
    public string LogPath { get; init; } = @"C:\Logs\Sentinel";
}

public sealed class ResourceThresholds
{
    public double CpuPercentThreshold { get; init; } = 85.0;
    public double RamPercentThreshold { get; init; } = 90.0;
    public double DiskFreePercentThreshold { get; init; } = 10.0;
    public string[] MonitoredDrives { get; init; } = ["C:\\"];
}

public sealed class NetworkOptions
{
    public string[] PingTargets { get; init; } = ["8.8.8.8", "1.1.1.1"];
    public int LatencyThresholdMs { get; init; } = 200;
}

public sealed class FileSystemOptions
{
    public string[] WatchPaths { get; init; } = [];
    public bool IncludeSubdirectories { get; init; } = true;
}

public sealed class EventLogOptions
{
    public string[] LogNames { get; init; } = ["System", "Application"];
}