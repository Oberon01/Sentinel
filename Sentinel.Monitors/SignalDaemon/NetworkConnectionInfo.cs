namespace Sentinel.Monitors.SignalDaemon;

public sealed record NetworkConnectionInfo(
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    bool IsEstablished,
    int Pid,
    string ProcessName
);