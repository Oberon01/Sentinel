namespace Sentinel.Core;

public sealed class SignalDaemonOptions
{
    public bool Enabled { get; init; } = false;
    public string BlocklistDbPath { get; init; } = "";
    public string DetectionsDbPath { get; init; } = "detections.sqlite";
    public string MatchLogPath { get; init; } = "matches.log";
    public SignalScanOptions Scan { get; init; } = new();
    public SignalNotifyOptions Notify { get; init; } = new();
}

public sealed class SignalScanOptions
{
    public bool OnlyEstablished { get; init; } = true;
    public bool ExternalOnly { get; init; } = true;
    public bool LogAllBaselines { get; init; } = false;
    public bool DedupeBaselineSession { get; init; } = true;
    public string SeverityThreshold { get; init; } = "High";
    public bool LogBaselines { get; init; } = false;
}

public sealed class SignalNotifyOptions
{
    public bool Enabled { get; init; } = true;
    public string MinSeverity { get; init; } = "Medium";
    public string[] Categories { get; init; } = [];
    public double SquelchSeconds { get; init; } = 180;
}