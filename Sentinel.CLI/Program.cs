using Microsoft.Extensions.Logging.Abstractions;
using Sentinel.CLI;
using Sentinel.Monitors.SignalDaemon;

const string DefaultBlocklistDb  = @"C:\Sentinel\signaldaemon_blocklist.sqlite";
const string DefaultDetectionsDb = @"C:\Logs\Sentinel\detections.sqlite";

if (args.Length < 2) { PrintUsage(); return 1; }

return (args[0], args[1]) switch
{
    ("blocklist",  "list")    => BlocklistList(args[2..]),
    ("blocklist",  "add")     => BlocklistAdd(args[2..]),
    ("blocklist",  "remove")  => BlocklistRemove(args[2..]),
    ("detections", "list")    => DetectionsList(args[2..]),
    ("detections", "summary") => DetectionsSummary(args[2..]),
    _                         => Unknown(args[0], args[1]),
};

// ── blocklist list ────────────────────────────────────────────────────────────

static int BlocklistList(string[] args)
{
    var db = Flag(args, "--db") ?? DefaultBlocklistDb;
    var repo = new BlocklistRepository(db);
    var entries = repo.GetAll();
    if (entries.Count == 0) { Console.WriteLine("No entries found."); return 0; }
    TablePrinter.Print(
        ["Domain", "IP Address", "Category", "Severity"],
        entries.Select(e => new[] { e.Domain, e.IpAddress ?? "", e.Category, e.Severity }).ToArray()
    );
    Console.WriteLine($"\n{entries.Count} entry/entries.");
    return 0;
}

// ── blocklist add ─────────────────────────────────────────────────────────────

static int BlocklistAdd(string[] args)
{
    var db       = Flag(args, "--db")       ?? DefaultBlocklistDb;
    var domain   = Flag(args, "--domain");
    var ip       = Flag(args, "--ip");
    var category = Flag(args, "--category");
    var severity = Flag(args, "--severity");

    if (domain is null || category is null || severity is null)
    {
        Console.Error.WriteLine("Usage: blocklist add --domain <d> --category <cat> --severity <sev> [--ip <ip>] [--db <path>]");
        return 1;
    }
    if (!new[] { "Low", "Medium", "High" }.Contains(severity, StringComparer.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"Invalid severity '{severity}'. Must be one of: Low, Medium, High.");
        return 1;
    }

    var repo = new BlocklistRepository(db);
    if (repo.Exists(domain))
    {
        Console.Error.WriteLine($"Entry for '{domain}' already exists. Remove it first to update.");
        return 1;
    }

    repo.Add(new BlocklistEntry(domain, ip, category, severity));
    Console.WriteLine($"Added: {domain} [{category}/{severity}]");
    return 0;
}

// ── blocklist remove ──────────────────────────────────────────────────────────

static int BlocklistRemove(string[] args)
{
    var db     = Flag(args, "--db")     ?? DefaultBlocklistDb;
    var domain = Flag(args, "--domain");

    if (domain is null)
    {
        Console.Error.WriteLine("Usage: blocklist remove --domain <d> [--db <path>]");
        return 1;
    }

    var repo = new BlocklistRepository(db);
    if (repo.Remove(domain))
    {
        Console.WriteLine($"Removed: {domain}");
        return 0;
    }

    Console.Error.WriteLine($"No entry found for '{domain}'.");
    return 1;
}

// ── detections list ───────────────────────────────────────────────────────────

static int DetectionsList(string[] args)
{
    var db       = Flag(args, "--db")       ?? DefaultDetectionsDb;
    var category = Flag(args, "--category");
    var severity = Flag(args, "--severity");
    var limit    = int.TryParse(Flag(args, "--limit"), out var l) ? l : 50;

    var repo    = new DetectionRepository(db, NullLogger<DetectionRepository>.Instance);
    var records = repo.GetRecent(limit, category, severity);
    if (records.Count == 0) { Console.WriteLine("No detections found."); return 0; }

    TablePrinter.Print(
        ["Timestamp", "Process", "Remote IP", "Matched Domain", "Category", "Severity"],
        records.Select(r => new[]
        {
            r.Timestamp, r.ProcessName, r.RemoteAddress,
            r.MatchedDomain, r.Category, r.Severity
        }).ToArray()
    );
    Console.WriteLine($"\n{records.Count} record(s).");
    return 0;
}

// ── detections summary ────────────────────────────────────────────────────────

static int DetectionsSummary(string[] args)
{
    var db      = Flag(args, "--db") ?? DefaultDetectionsDb;
    var repo    = new DetectionRepository(db, NullLogger<DetectionRepository>.Instance);
    var summary = repo.GetSummary();

    Console.WriteLine("By Process:");
    if (summary.ByProcess.Count == 0)
        Console.WriteLine("  (none)");
    else
        TablePrinter.Print(["Process", "Count"],
            summary.ByProcess.Select(p => new[] { p.ProcessName, p.Count.ToString() }).ToArray());

    Console.WriteLine();
    Console.WriteLine("By Category:");
    if (summary.ByCategory.Count == 0)
        Console.WriteLine("  (none)");
    else
        TablePrinter.Print(["Category", "Count"],
            summary.ByCategory.Select(c => new[] { c.Category, c.Count.ToString() }).ToArray());

    Console.WriteLine();
    Console.WriteLine("By Severity:");
    if (summary.BySeverity.Count == 0)
        Console.WriteLine("  (none)");
    else
        TablePrinter.Print(["Severity", "Count"],
            summary.BySeverity.Select(s => new[] { s.Severity, s.Count.ToString() }).ToArray());

    return 0;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

static string? Flag(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int Unknown(string cmd, string sub)
{
    Console.Error.WriteLine($"Unknown command: {cmd} {sub}");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        Sentinel CLI — blocklist and detection management

        Usage:
          sentinel-cli blocklist  list     [--db <path>]
          sentinel-cli blocklist  add      --domain <d> --category <cat> --severity <sev> [--ip <ip>] [--db <path>]
          sentinel-cli blocklist  remove   --domain <d> [--db <path>]
          sentinel-cli detections list     [--db <path>] [--limit <n>] [--category <cat>] [--severity <sev>]
          sentinel-cli detections summary  [--db <path>]
        """);
}
