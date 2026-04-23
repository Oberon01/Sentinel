using Sentinel.Core;
using Microsoft.Extensions.Logging;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Options;

namespace Sentinel.Monitors;

public sealed class NetworkMonitor : IMonitor
{
    public string Name => "Network";

    private readonly SentinelOptions _options;
    private readonly ILogger<NetworkMonitor> _logger;

    public NetworkMonitor(IOptions<SentinelOptions> options, ILogger<NetworkMonitor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MonitorResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var metrics = new Dictionary<string, object>();
        var issues = new List<string>();

        foreach (var target in _options.Network.PingTargets)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(target, 3000);

                if (reply.Status == IPStatus.Success)
                {
                    metrics[$"ping_{target}_ms"] = reply.RoundtripTime;

                    if (reply.RoundtripTime > _options.Network.LatencyThresholdMs)
                        issues.Add($"{target} latency {reply.RoundtripTime}ms");
                }
                else
                {
                    metrics[$"ping_{target}_ms"] = -1;
                    issues.Add($"{target} unreachable ({reply.Status})");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ping failed for {Target}", target);
                metrics[$"ping_{target}_ms"] = -1;
                issues.Add($"{target} ping error");
            }
        }

        if (issues.Count > 0)
            return MonitorResult.Unhealthy(Name, string.Join(", ", issues), metrics);

        var summary = string.Join(" | ", _options.Network.PingTargets
            .Distinct()
            .Select(t => $"{t}: {metrics.GetValueOrDefault($"ping_{t}_ms", "?")}ms"));

        return MonitorResult.Healthy(Name, summary, metrics);
    }
}