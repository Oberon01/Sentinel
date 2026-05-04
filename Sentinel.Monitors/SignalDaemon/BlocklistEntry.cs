namespace Sentinel.Monitors.SignalDaemon;

public sealed record BlocklistEntry(
    string Domain,
    string? IpAddress,
    string Category,
    string Severity
);