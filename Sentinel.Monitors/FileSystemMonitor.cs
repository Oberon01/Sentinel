using Sentinel.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sentinel.Monitors;

public sealed class FileSystemMonitor : IMonitor, IDisposable
{
    public string Name => "FileSystem";

    private readonly ILogger<FileSystemMonitor> _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Queue<string> _recentEvents = new();
    private readonly Lock _lock = new();

    public FileSystemMonitor(IOptions<SentinelOptions> options, ILogger<FileSystemMonitor> logger)
    {
        _logger = logger;

        foreach (var path in options.Value.FileSystem.WatchPaths)
        {
            if (!Directory.Exists(path))
            {
                _logger.LogWarning("Watch path does not exist, skipping: {Path}", path);
                continue;
            }

            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = options.Value.FileSystem.IncludeSubdirectories,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
            };

            watcher.Created += (_, e) => OnEvent("Created", e.FullPath);
            watcher.Changed += (_, e) => OnEvent("Changed", e.FullPath);
            watcher.Deleted += (_, e) => OnEvent("Deleted", e.FullPath);
            watcher.Renamed += (_, e) => OnEvent("Renamed", $"{e.OldFullPath} → {e.FullPath}");

            _watchers.Add(watcher);
            _logger.LogInformation("Watching: {Path}", path);
        }
    }

    public Task<MonitorResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        List<string> snapshot;

        lock (_lock)
        {
            snapshot = _recentEvents.ToList();
            _recentEvents.Clear();
        }

        var metrics = new Dictionary<string, object>
        {
            ["events_since_last_check"] = snapshot.Count
        };

        var summary = snapshot.Count > 0
            ? $"{snapshot.Count} file event(s) detected"
            : "No file changes detected";

        // File system changes are informational — always healthy, just logged
        return Task.FromResult(MonitorResult.Healthy(Name, summary, metrics));
    }

    private void OnEvent(string type, string path)
    {
        _logger.LogInformation("[FileSystem] {Type}: {Path}", type, path);

        lock (_lock)
        {
            _recentEvents.Enqueue($"{type}: {path}");
            if (_recentEvents.Count > 100)
                _recentEvents.Dequeue();
        }
    }

    public void Dispose()
    {
        foreach (var w in _watchers)
            w.Dispose();
    }
}