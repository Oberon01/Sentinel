namespace Sentinel.CLI;

internal static class TablePrinter
{
    internal static void Print(string[] headers, string[][] rows)
    {
        var widths = new int[headers.Length];
        for (int i = 0; i < headers.Length; i++)
            widths[i] = headers[i].Length;

        foreach (var row in rows)
            for (int i = 0; i < Math.Min(row.Length, widths.Length); i++)
                widths[i] = Math.Max(widths[i], row[i].Length);

        var separator = string.Join("  ", widths.Select(w => new string('-', w)));

        Console.WriteLine(string.Join("  ", headers.Select((h, i) => h.PadRight(widths[i]))));
        Console.WriteLine(separator);

        foreach (var row in rows)
            Console.WriteLine(string.Join("  ", row.Select((c, i) => i < widths.Length ? c.PadRight(widths[i]) : c)));
    }
}
