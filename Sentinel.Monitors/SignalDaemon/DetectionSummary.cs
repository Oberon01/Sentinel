namespace Sentinel.Monitors.SignalDaemon;

public sealed record DetectionSummary(
    IReadOnlyList<(string ProcessName, int Count)> ByProcess,
    IReadOnlyList<(string Category, int Count)> ByCategory,
    IReadOnlyList<(string Severity, int Count)> BySeverity
);
