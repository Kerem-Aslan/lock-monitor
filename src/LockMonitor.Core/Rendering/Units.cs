using System.Globalization;

namespace LockMonitor.Rendering;

/// <summary>Short, invariant formatting for the cards (the UI is English only).</summary>
public static class Units
{
    private const double KiB = 1024;
    private const double MiB = KiB * 1024;
    private const double GiB = MiB * 1024;
    private const double TiB = GiB * 1024;

    /// <summary>Binary sizes with Windows-style labels: 512 MB, 2.9 GB, 15.4 GB, 1.8 TB.</summary>
    public static string Bytes(ulong bytes) => bytes switch
    {
        < (ulong)MiB => $"{Fmt(bytes / KiB, 0)} KB",
        < (ulong)GiB => $"{Fmt(bytes / MiB, 0)} MB",
        < (ulong)TiB => $"{Fmt(bytes / GiB, 1)} GB",
        _ => $"{Fmt(bytes / TiB, 1)} TB",
    };

    /// <summary>"2.9 / 8.0 GB": both sides in the unit of the total.</summary>
    public static string UsedOfTotal(ulong used, ulong total)
    {
        var (divisor, unit, decimals) = total switch
        {
            < (ulong)GiB => (MiB, "MB", 0),
            < (ulong)TiB => (GiB, "GB", 1),
            _ => (TiB, "TB", 1),
        };
        return $"{Fmt(used / divisor, decimals)} / {Fmt(total / divisor, decimals)} {unit}";
    }

    /// <summary>Transfer rate in bytes per second: 0 KB/s, 850 KB/s, 1.2 MB/s, 48 MB/s.</summary>
    public static string Rate(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond < 0)
        {
            bytesPerSecond = 0;
        }
        return bytesPerSecond switch
        {
            < MiB => $"{Fmt(bytesPerSecond / KiB, 0)} KB/s",
            < 10 * MiB => $"{Fmt(bytesPerSecond / MiB, 1)} MB/s",
            < GiB => $"{Fmt(bytesPerSecond / MiB, 0)} MB/s",
            _ => $"{Fmt(bytesPerSecond / GiB, 1)} GB/s",
        };
    }

    public static string Celsius(double celsius) => $"{Fmt(celsius, 0)} °C";

    private static string Fmt(double value, int decimals) =>
        Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString(decimals == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);
}
