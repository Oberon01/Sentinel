using Sentinel.Core;
using Sentinel.Monitors;
using Microsoft.Extensions.Options;

namespace Sentinel.Service;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly AlertService _alertService;
    private readonly SentinelOptions _options;
    private readonly IEnumerable<IMonitor> _monitors;
    private readonly ConfigWatcher _configWatcher;

    public Worker(
        ILogger<Worker> logger,
        AlertService alertService,
        IOptions<SentinelOptions> options,
        IEnumerable<IMonitor> monitors,
        ConfigWatcher configWatcher)
    {
        _logger = logger;
        _alertService = alertService;
        _options = options.Value;
        _monitors = monitors;
        _configWatcher = configWatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _configWatcher.Start(configPath);

        _logger.LogInformation("Sentinel started at {Time}", DateTimeOffset.UtcNow);

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var monitor in _monitors)
            {
                try
                {
                    var result = await monitor.CheckAsync(stoppingToken);

                    if (result.IsHealthy)
                    {
                        _logger.LogInformation("[{Monitor}] {Summary}", result.MonitorName, result.Summary);
                    }
                    else
                    {
                        _logger.LogWarning("[{Monitor}] UNHEALTHY — {Summary}", result.MonitorName, result.Summary);
                        _alertService.SendAlert($"Sentinel — {result.MonitorName}", result.Summary);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Monitor [{Monitor}] threw an unhandled exception", monitor.Name);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
        }

        _configWatcher.Dispose();
        _logger.LogInformation("Sentinel stopping at {Time}", DateTimeOffset.UtcNow);
    }
}