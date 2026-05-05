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

    public IReadOnlyList<DetectionRecord> GetRecent(int limit = 50, string? category = null, string? severity = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ts, process_name, pid, laddr, lport, raddr, rport,
                   dest_ip, dest_domain, matched_domain, category, severity, match_type
            FROM   detections
            WHERE  ($cat IS NULL OR category = $cat)
              AND  ($sev IS NULL OR severity = $sev)
            ORDER  BY id DESC
            LIMIT  $limit
            """;
        cmd.Parameters.AddWithValue("$cat", (object?)category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sev", (object?)severity ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$limit", limit);

        var results = new List<DetectionRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new DetectionRecord(
                reader.GetString(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                reader.IsDBNull(7) ? "" : reader.GetString(7),
                reader.IsDBNull(8) ? "" : reader.GetString(8),
                reader.IsDBNull(9) ? "" : reader.GetString(9),
                reader.IsDBNull(10) ? "" : reader.GetString(10),
                reader.IsDBNull(11) ? "" : reader.GetString(11),
                reader.IsDBNull(12) ? "" : reader.GetString(12)
            ));
        }
        return results;
    }

    public DetectionSummary GetSummary()
    {
        using var conn = Open();

        static List<(string, int)> RunGroupBy(SqliteConnection c, string column)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"""
                SELECT {column}, COUNT(*) AS cnt
                FROM   detections
                GROUP  BY {column}
                ORDER  BY cnt DESC
                """;
            var rows = new List<(string, int)>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.IsDBNull(0) ? "" : reader.GetString(0), reader.GetInt32(1)));
            return rows;
        }

        return new DetectionSummary(
            RunGroupBy(conn, "process_name"),
            RunGroupBy(conn, "category"),
            RunGroupBy(conn, "severity")
        );
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }
}