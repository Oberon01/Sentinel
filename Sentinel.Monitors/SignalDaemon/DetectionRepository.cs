using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Sentinel.Monitors.SignalDaemon;

public sealed class DetectionRepository
{
    private readonly string _dbPath;
    private readonly ILogger<DetectionRepository> _logger;

    public DetectionRepository(string dbPath, ILogger<DetectionRepository> logger)
    {
        _dbPath = dbPath;
        _logger = logger;
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS detections (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                ts             TEXT NOT NULL,
                process_name   TEXT,
                pid            INTEGER,
                laddr          TEXT,
                lport          INTEGER,
                raddr          TEXT,
                rport          INTEGER,
                dest_ip        TEXT,
                dest_domain    TEXT,
                matched_domain TEXT,
                category       TEXT,
                severity       TEXT,
                match_type     TEXT DEFAULT 'match'
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void Insert(DetectionRecord r)
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO detections
                    (ts, process_name, pid, laddr, lport, raddr, rport,
                     dest_ip, dest_domain, matched_domain, category, severity, match_type)
                VALUES
                    ($ts, $pn, $pid, $la, $lp, $ra, $rp,
                     $di, $dd, $md, $cat, $sev, $mt)
                """;
            cmd.Parameters.AddWithValue("$ts", r.Timestamp);
            cmd.Parameters.AddWithValue("$pn", r.ProcessName);
            cmd.Parameters.AddWithValue("$pid", r.Pid);
            cmd.Parameters.AddWithValue("$la", r.LocalAddress);
            cmd.Parameters.AddWithValue("$lp", r.LocalPort);
            cmd.Parameters.AddWithValue("$ra", r.RemoteAddress);
            cmd.Parameters.AddWithValue("$rp", r.RemotePort);
            cmd.Parameters.AddWithValue("$di", r.DestinationIp);
            cmd.Parameters.AddWithValue("$dd", r.DestinationDomain);
            cmd.Parameters.AddWithValue("$md", r.MatchedDomain);
            cmd.Parameters.AddWithValue("$cat", r.Category);
            cmd.Parameters.AddWithValue("$sev", r.Severity);
            cmd.Parameters.AddWithValue("$mt", r.MatchType);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert detection record");
        }
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }
}