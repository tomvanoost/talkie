using System.Globalization;

namespace Talkie.Shared;

/// <summary>
/// Formats large numbers into a compact form: 1000 = 1K, 1000000 = 1M, then B, T and Q.
/// </summary>
public static class NumberFormatter
{
    private static readonly (decimal Threshold, string Suffix)[] Units =
    [
        (1_000_000_000_000_000m, "Q"),
        (1_000_000_000_000m, "T"),
        (1_000_000_000m, "B"),
        (1_000_000m, "M"),
        (1_000m, "K"),
    ];

    public static string Compact(decimal value)
    {
        var sign = value < 0 ? "-" : "";
        var abs = Math.Abs(value);

        foreach (var (threshold, suffix) in Units)
        {
            if (abs >= threshold)
            {
                // Truncate (not round) to one decimal so 1999 shows as 1.9K and never as 2K.
                var scaled = Math.Floor(abs / threshold * 10m) / 10m;
                return sign + scaled.ToString("0.#", CultureInfo.InvariantCulture) + suffix;
            }
        }

        return sign + Math.Floor(abs).ToString("0", CultureInfo.InvariantCulture);
    }

    public static string Compact(long value) => Compact((decimal)value);
    public static string Compact(int value) => Compact((decimal)value);

    public static string Money(long value) => "$" + Compact(value);

    public static string Duration(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
    }
}
