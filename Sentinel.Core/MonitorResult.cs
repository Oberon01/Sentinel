namespace Sentinel.Core;

public sealed record MonitorResult(
    string MonitorName,
    bool IsHealthy,
    string Summary,
    IReadOnlyDictionary<string, object> Metrics,
    DateTimeOffset Timestamp)
{
    public static MonitorResult Healthy(string monitorName, string summary, Dictionary<string, object>? metrics = null) =>
        new(monitorName, true, summary, metrics ?? new Dictionary<string, object>(), DateTimeOffset.UtcNow);

    public static MonitorResult Unhealthy(string monitorName, string summary, Dictionary<string, object>? metrics = null) =>
        new(monitorName, false, summary, metrics ?? new Dictionary<string, object>(), DateTimeOffset.UtcNow);
}