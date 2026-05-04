namespace Sentinel.Monitors.SignalDaemon;

public sealed record DetectionRecord(
    string Timestamp,
    string ProcessName,
    int Pid,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string DestinationIp,
    string DestinationDomain,
    string MatchedDomain,
    string Category,
    string Severity,
    string MatchType
);