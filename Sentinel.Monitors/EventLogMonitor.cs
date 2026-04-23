using Sentinel.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Sentinel.Monitors;

public sealed class EventLogMonitor : IMonitor
{
    public string Name => "EventLog";

    private readonly SentinelOptions _options;
    private readonly ILogger<EventLogMonitor> _logger;
    private DateTimeOffset _lastChecked = DateTimeOffset.UtcNow;

    public EventLogMonitor(IOptions<SentinelOptions> options, ILogger<EventLogMonitor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<MonitorResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var checkFrom = _lastChecked;
        _lastChecked = DateTimeOffset.UtcNow;

        foreach (var logName in _options.EventLog.LogNames)
        {
            try
            {
                using var log = new EventLog(logName);

                var recent = log.Entries
                    .Cast<EventLogEntry>()
                    .Where(e => e.TimeGenerated.ToUniversalTime() >= checkFrom.UtcDateTime
                             && e.EntryType is EventLogEntryType.Error or EventLogEntryType.FailureAudit)
                    .ToList();

                foreach (var entry in recent)
                {
                    var msg = $"[{logName}] {entry.Source}: {entry.Message[..Math.Min(100, entry.Message.Length)]}";
                    errors.Add(msg);
                    _logger.LogWarning("EventLog Error: {Message}", msg);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read event log: {LogName}", logName);
            }
        }

        var metrics = new Dictionary<string, object>
        {
            ["errors_since_last_check"] = errors.Count
        };

        if (errors.Count > 0)
            return Task.FromResult(MonitorResult.Unhealthy(Name, $"{errors.Count} error(s) in Event Log", metrics));

        return Task.FromResult(MonitorResult.Healthy(Name, "No new errors in Event Log", metrics));
    }
}