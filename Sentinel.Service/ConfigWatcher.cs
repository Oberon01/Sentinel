using Microsoft.Extensions.Options;

namespace Sentinel.Service;

public sealed class ConfigWatcher : IDisposable
{
    private readonly ILogger<ConfigWatcher> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);

    public ConfigWatcher(ILogger<ConfigWatcher> logger, IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _lifetime = lifetime;
    }

    public void Start(string configPath)
    {
        var directory = Path.GetDirectoryName(configPath)!;
        var filename = Path.GetFileName(configPath);

        _watcher = new FileSystemWatcher(directory, filename)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnConfigChanged;
        _logger.LogInformation("Watching config file for changes: {Path}", configPath);
    }

    private void OnConfigChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce — editors often fire multiple events on a single save
        _debounce?.Dispose();
        _debounce = new Timer(_ =>
        {
            if (!IsValidJson(e.FullPath))
            {
                _logger.LogError("Config change detected but JSON is invalid — ignoring. Fix {Path} and save again.", e.FullPath);
                return;
            }

            _logger.LogInformation("Valid config change detected — restarting service to apply changes.");
            Environment.Exit(1); // Let the host restart with the new config

        }, null, DebounceDelay, Timeout.InfiniteTimeSpan);
    }

    private static bool IsValidJson(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            System.Text.Json.JsonDocument.Parse(json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _debounce?.Dispose();
        _watcher?.Dispose();
    }
}