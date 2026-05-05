using Microsoft.Data.Sqlite;

namespace Sentinel.Monitors.SignalDaemon;

public sealed class BlocklistRepository
{
    private readonly string _dbPath;

    public BlocklistRepository(string dbPath)
    {
        _dbPath = dbPath;
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS blocklist (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                domain     TEXT NOT NULL,
                ip_address TEXT,
                category   TEXT NOT NULL,
                severity   TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<BlocklistEntry> GetAll()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT domain, ip_address, category, severity FROM blocklist ORDER BY domain ASC";

        var results = new List<BlocklistEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new BlocklistEntry(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)
            ));
        }
        return results;
    }

    public bool Exists(string domain)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM blocklist WHERE domain = $domain COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$domain", domain);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public void Add(BlocklistEntry entry)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO blocklist (domain, ip_address, category, severity)
            VALUES ($domain, $ip, $category, $severity)
            """;
        cmd.Parameters.AddWithValue("$domain", entry.Domain);
        cmd.Parameters.AddWithValue("$ip", (object?)entry.IpAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$category", entry.Category);
        cmd.Parameters.AddWithValue("$severity", entry.Severity);
        cmd.ExecuteNonQuery();
    }

    public bool Remove(string domain)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM blocklist WHERE domain = $domain COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$domain", domain);
        return cmd.ExecuteNonQuery() > 0;
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }
}
