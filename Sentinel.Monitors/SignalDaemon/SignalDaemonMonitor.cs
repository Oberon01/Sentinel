using Sentinel.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;

namespace Sentinel.Monitors.SignalDaemon;

public sealed class SignalDaemonMonitor : IMonitor
{
    public string Name => "SignalDaemon";

    private readonly SignalDaemonOptions _options;
    private readonly AlertService _alertService;
    private readonly ILogger<SignalDaemonMonitor> _logger;
    private readonly BlocklistService _blocklist;
    private readonly NetworkConnectionScanner _scanner;
    private readonly DetectionRepository? _repo;

    // Per-destination notification squelch (key = "process|matchedDomain")
    private readonly Dictionary<string, DateTimeOffset> _notifySquelch = new();
    // Baseline deduplication across the session
    private readonly HashSet<(int Pid, string LA, int LP, string RA, int RP)> _baselineSeen = new();
    // rDNS cache keyed by remote IP
    private readonly Dictionary<string, (DateTimeOffset CachedAt, string HostName)> _rdnsCache = new();
    private static readonly TimeSpan RdnsCacheTtl = TimeSpan.FromMinutes(10);

    private static readonly IReadOnlyDictionary<string, int> SevOrder =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        { ["Low"] = 1, ["Medium"] = 2, ["High"] = 3 };

    public SignalDaemonMonitor(
        IOptions<SentinelOptions> options,
        AlertService alertService,
        ILogger<SignalDaemonMonitor> logger,
        ILoggerFactory loggerFactory,
        NetworkConnectionScanner scanner)
    {
        _options = options.Value.SignalDaemon;
        _alertService = alertService;
        _logger = logger;
        _scanner = scanner;
        _blocklist = new BlocklistService(loggerFactory.CreateLogger<BlocklistService>());

        if (!_options.Enabled || string.IsNullOrEmpty(_options.BlocklistDbPath))
            return;

        if (!File.Exists(_options.BlocklistDbPath))
        {
            _logger.LogError("SignalDaemon blocklist database not found: {Path}", _options.BlocklistDbPath);
            return;
        }

        _blocklist.LoadFromSqlite(_options.BlocklistDbPath);
        _blocklist.PreResolveDns();

        if (!string.IsNullOrEmpty(_options.DetectionsDbPath))
            _repo = new DetectionRepository(
                _options.DetectionsDbPath,
                loggerFactory.CreateLogger<DetectionRepository>());
    }

    public Task<MonitorResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrEmpty(_options.BlocklistDbPath))
            return Task.FromResult(MonitorResult.Healthy(Name, "SignalDaemon disabled"));

        int threshold = SevOrder.GetValueOrDefault(_options.Scan.SeverityThreshold, 3);
        var conns = _scanner.GetConnections(_options.Scan.OnlyEstablished);

        int matches = 0, baselines = 0;
        var matchSummaries = new List<string>();

        foreach (var conn in conns)
        {
            if (_options.Scan.ExternalOnly && IsPrivateOrLoopback(conn.RemoteAddress))
                continue;

            string destDomain = ReverseDns(conn.RemoteAddress);
            var hit = _blocklist.MatchDomain(destDomain) ?? _blocklist.MatchIp(conn.RemoteAddress);
            string ts = DateTimeOffset.UtcNow.ToString("O");

            if (hit.HasValue)
            {
                var (matchedDomain, category, severity) = hit.Value;

                if (SevOrder.GetValueOrDefault(severity, 1) >= threshold)
                {
                    _logger.LogWarning(
                        "[SignalDaemon] {Process} (pid {Pid}) -> {RemoteIp} ({Domain}) :: {Matched} [{Cat}/{Sev}]",
                        conn.ProcessName, conn.Pid, conn.RemoteAddress,
                        string.IsNullOrEmpty(destDomain) ? "no-rdns" : destDomain,
                        matchedDomain, category, severity);

                    _repo?.Insert(new DetectionRecord(ts, conn.ProcessName, conn.Pid,
                        conn.LocalAddress, conn.LocalPort, conn.RemoteAddress, conn.RemotePort,
                        conn.RemoteAddress, destDomain, matchedDomain, category, severity, "match"));

                    WriteMatchLog(severity, conn.ProcessName, conn.Pid,
                        matchedDomain, destDomain, conn.RemoteAddress, category);

                    matches++;
                    matchSummaries.Add($"{conn.ProcessName}->{matchedDomain} [{category}/{severity}]");
                }

                if (_options.Notify.Enabled &&
                    SevOrder.GetValueOrDefault(severity, 1) >=
                        SevOrder.GetValueOrDefault(_options.Notify.MinSeverity, 2) &&
                    IsCategoryAllowed(category))
                {
                    TrySendAlert(conn.ProcessName, matchedDomain, destDomain,
                        conn.RemoteAddress, category, severity);
                }
            }
            else if (_options.Scan.LogAllBaselines)
            {
                var key = (conn.Pid, conn.LocalAddress, conn.LocalPort,
                           conn.RemoteAddress, conn.RemotePort);

                if (_options.Scan.DedupeBaselineSession && _baselineSeen.Contains(key))
                    continue;
                _baselineSeen.Add(key);

                _repo?.Insert(new DetectionRecord(ts, conn.ProcessName, conn.Pid,
                    conn.LocalAddress, conn.LocalPort, conn.RemoteAddress, conn.RemotePort,
                    conn.RemoteAddress, destDomain, "", "Baseline", "Low", "baseline"));

                if (_options.Scan.LogBaselines)
                    WriteMatchLog("Low", conn.ProcessName, conn.Pid,
                        !string.IsNullOrEmpty(destDomain) ? destDomain : conn.RemoteAddress,
                        "", conn.RemoteAddress, "Baseline", tag: "BASELINE");

                baselines++;
            }
        }

        var metrics = new Dictionary<string, object>
        {
            ["connections_scanned"] = conns.Count,
            ["matches"] = matches,
            ["baselines_logged"] = baselines
        };

        if (matches > 0)
        {
            var summary = $"{matches} blocklist match(es): " +
                          string.Join("; ", matchSummaries.Take(3));
            return Task.FromResult(MonitorResult.Unhealthy(Name, summary, metrics));
        }

        return Task.FromResult(MonitorResult.Healthy(Name,
            $"Scanned {conns.Count} connections — no blocklist matches", metrics));
    }

    private void TrySendAlert(string process, string matchedDomain, string destDomain,
        string remoteIp, string category, string severity)
    {
        var key = $"{process}|{matchedDomain}";
        var squelch = TimeSpan.FromSeconds(_options.Notify.SquelchSeconds);

        if (_notifySquelch.TryGetValue(key, out var last) &&
            DateTimeOffset.UtcNow - last < squelch)
            return;

        _notifySquelch[key] = DateTimeOffset.UtcNow;

        var host = !string.IsNullOrEmpty(matchedDomain) ? matchedDomain
                 : !string.IsNullOrEmpty(destDomain) ? destDomain
                 : remoteIp;

        _alertService.SendAlert(
            $"Sentinel — SignalDaemon [{severity.ToUpperInvariant()}] {category}",
            $"{process} → {host}");
    }

    private void WriteMatchLog(string severity, string process, int pid,
        string host, string destDomain, string remoteIp, string category, string tag = "")
    {
        if (string.IsNullOrEmpty(_options.MatchLogPath)) return;
        try
        {
            var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var displayHost = !string.IsNullOrEmpty(host) ? host
                            : !string.IsNullOrEmpty(destDomain) ? destDomain
                            : remoteIp;
            var suffix = string.IsNullOrEmpty(tag) ? "" : $" [{tag}]";
            var line = $"[{ts}] [{severity.ToUpperInvariant()}] {process} (pid {pid}) -> {displayHost} [{category}]{suffix}";

            var dir = Path.GetDirectoryName(Path.GetFullPath(_options.MatchLogPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(_options.MatchLogPath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write match log entry");
        }
    }

    private bool IsCategoryAllowed(string category)
    {
        if (_options.Notify.Categories.Length == 0) return true;
        return Array.Exists(_options.Notify.Categories,
            c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));
    }

    private string ReverseDns(string ip)
    {
        if (_rdnsCache.TryGetValue(ip, out var cached) &&
            DateTimeOffset.UtcNow - cached.CachedAt < RdnsCacheTtl)
            return cached.HostName;

        string hostName = "";
        try { hostName = Dns.GetHostEntry(ip).HostName; }
        catch { }

        _rdnsCache[ip] = (DateTimeOffset.UtcNow, hostName);
        return hostName;
    }

    private static bool IsPrivateOrLoopback(string ipStr)
    {
        try
        {
            var addr = IPAddress.Parse(ipStr);
            if (IPAddress.IsLoopback(addr)) return true;
            if (addr.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = addr.GetAddressBytes();
                return b[0] == 10
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    || (b[0] == 192 && b[1] == 168)
                    || b[0] == 169;
            }
            return false;
        }
        catch { return false; }
    }
}