using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace Sentinel.Monitors.SignalDaemon;

public sealed class BlocklistService
{
    private readonly ILogger<BlocklistService> _logger;

    private readonly HashSet<string> _domains = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _domainSuffixes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ips = new();
    private readonly Dictionary<string, (string Category, string Severity)> _meta
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTimeOffset CachedAt, HashSet<string> Ips)> _dnsCache = new();
    private static readonly TimeSpan DnsTtl = TimeSpan.FromHours(1);

    public BlocklistService(ILogger<BlocklistService> logger)
    {
        _logger = logger;
    }

    public int LoadFromSqlite(string path, string table = "blocklist")
    {
        if (!File.Exists(path))
        {
            _logger.LogError("Blocklist SQLite not found: {Path}", path);
            return 0;
        }

        int count = 0;
        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT domain, ip_address, category, severity FROM {table}";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var domain = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim().ToLowerInvariant();
            var ip = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
            var cat = reader.IsDBNull(2) ? "Unknown" : reader.GetString(2);
            var sev = reader.IsDBNull(3) ? "Low" : reader.GetString(3);

            if (!string.IsNullOrEmpty(domain))
            {
                _domains.Add(domain);
                _domainSuffixes.Add(domain.StartsWith('.') ? domain : "." + domain);
                _meta[domain] = (cat, sev);
                count++;
            }

            if (!string.IsNullOrEmpty(ip))
                _ips.Add(ip);
        }

        _logger.LogInformation("Blocklist loaded: {Count} entries from {Path}", count, path);
        return count;
    }

    public void PreResolveDns(int limit = 600)
    {
        int n = 0;
        foreach (var domain in _domains)
        {
            if (n >= limit) break;
            DnsLookup(domain);
            n++;
        }
        _logger.LogInformation("Pre-resolved DNS for {Count} blocklist domains", n);
    }

    public (string MatchedDomain, string Category, string Severity)? MatchDomain(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return null;
        var d = domain.ToLowerInvariant().Trim();

        if (_domains.Contains(d))
        {
            var (cat, sev) = _meta.GetValueOrDefault(d, ("Unknown", "Low"));
            return (d, cat, sev);
        }

        foreach (var suffix in _domainSuffixes)
        {
            if (d.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var baseDomain = suffix[1..];
                var (cat, sev) = _meta.GetValueOrDefault(baseDomain, ("Unknown", "Low"));
                return (baseDomain, cat, sev);
            }
        }

        return null;
    }

    public (string MatchedDomain, string Category, string Severity)? MatchIp(string ip)
    {
        if (_ips.Contains(ip))
            return (ip, "Unknown", "Medium");

        foreach (var (domain, (cat, sev)) in _meta)
        {
            var resolvedIps = DnsLookup(domain);
            if (resolvedIps.Contains(ip))
                return (domain, cat, sev);
        }

        return null;
    }

    private HashSet<string> DnsLookup(string domain)
    {
        if (_dnsCache.TryGetValue(domain, out var cached) &&
            DateTimeOffset.UtcNow - cached.CachedAt < DnsTtl)
            return cached.Ips;

        var ips = new HashSet<string>();
        try
        {
            var entry = Dns.GetHostEntry(domain);
            foreach (var addr in entry.AddressList)
                ips.Add(addr.ToString());
        }
        catch (SocketException) { }
        catch (ArgumentException) { }

        _dnsCache[domain] = (DateTimeOffset.UtcNow, ips);
        return ips;
    }
}