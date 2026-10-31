using System.Globalization;
using System.Text.Json.Nodes;
using LockMonitor.Sampling;

namespace LockMonitor.Rendering;

/// <summary>Widget sizes as the cards know them (mirrors the host's small/medium/large).</summary>
public enum CardSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

/// <summary>What a single widget chose to show.</summary>
/// <param name="GpuLuid">Adapter to show; null = the default adapter.</param>
/// <param name="ShowGpuTemperature">Whether the GPU temperature row is shown (medium/large).</param>
/// <param name="Drives">Drive names to show ("C:"); null = the system drive.</param>
public sealed record CardOptions(long? GpuLuid = null, bool ShowGpuTemperature = true, IReadOnlyList<string>? Drives = null);

/// <summary>
/// Builds the Adaptive Card data payload the templates bind to:
/// <c>metrics</c> = CPU/RAM/GPU headline values (all sizes), <c>details</c> = extra rows for
/// medium/large cards, <c>gpuName</c> = the adapter shown.
/// </summary>
public static class CardData
{
    /// <summary>Usage above this percentage is highlighted.</summary>
    public const double HighUsageThreshold = 85.0;

    /// <summary>GPU temperature at or above this is highlighted.</summary>
    public const double HighGpuTemperature = 85.0;

    /// <summary>Shown when a sensor cannot provide a value on this machine.</summary>
    public const string NotAvailable = "N/A";

    /// <summary>Shown before the first sample arrives.</summary>
    public const string Pending = "--";

    public readonly record struct Metric(string Label, double? Percent, bool IsPending = false);

    /// <summary>Card data for a snapshot; a null snapshot means "no reading yet".</summary>
    public static string Build(SystemSnapshot? snapshot, CardSize size, CardOptions? cardOptions = null)
    {
        // A record class, not a struct: a struct default would silently turn ShowGpuTemperature off.
        var options = cardOptions ?? new CardOptions();
        bool pending = snapshot is null;
        var gpu = SelectGpu(snapshot, options.GpuLuid);

        var root = new JsonObject
        {
            ["metrics"] = Rows(
            [
                new Metric("CPU", snapshot?.CpuPercent, pending),
                new Metric("RAM", snapshot?.MemoryPercent, pending),
                new Metric("GPU", gpu?.UsagePercent, pending),
            ]),
            ["gpuName"] = gpu?.Name ?? "",
        };

        if (size >= CardSize.Medium)
        {
            root["details"] = Details(snapshot, gpu, options);
        }
        return root.ToJsonString();
    }

    /// <summary>Phase-1 style payload from explicit metrics (used by tests and the static preview).</summary>
    public static string Build(IEnumerable<Metric> metrics) =>
        new JsonObject { ["metrics"] = Rows(metrics) }.ToJsonString();

    public static GpuReading? SelectGpu(SystemSnapshot? snapshot, long? luid)
    {
        if (snapshot is null)
        {
            return null;
        }
        return (luid is { } l ? snapshot.FindGpu(l) : null)
            ?? (snapshot.DefaultGpuLuid is { } d ? snapshot.FindGpu(d) : null)
            ?? snapshot.Gpus.FirstOrDefault();
    }

    private static JsonArray Rows(IEnumerable<Metric> metrics)
    {
        var rows = new JsonArray();
        foreach (var m in metrics)
        {
            rows.Add((JsonNode)Row(m));
        }
        return rows;
    }

    public static JsonObject Row(Metric m)
    {
        bool high = !m.IsPending && IsHigh(m.Percent);
        var row = new JsonObject
        {
            ["label"] = m.Label,
            ["value"] = m.IsPending ? Pending : FormatPercent(m.Percent),
        };
        AddEmphasis(row, high);
        AddBar(row, m.IsPending ? null : m.Percent, high);
        return row;
    }

    private static JsonArray Details(SystemSnapshot? s, GpuReading? gpu, CardOptions options)
    {
        bool pending = s is null;
        var rows = new JsonArray();

        if (options.ShowGpuTemperature)
        {
            double? t = gpu?.TemperatureCelsius;
            rows.Add((JsonNode)Detail("GPU temp", pending ? Pending : t is { } c ? Units.Celsius(c) : NotAvailable,
                high: t >= HighGpuTemperature));
        }

        string memLabel = gpu?.MemoryIsShared == true ? "GPU memory" : "VRAM";
        string vram = pending ? Pending
            : gpu is { MemoryUsedBytes: { } used, MemoryTotalBytes: { } total } ? Units.UsedOfTotal(used, total)
            : NotAvailable;
        rows.Add((JsonNode)Detail(memLabel, vram, percent: gpu?.MemoryPercent, high: IsHigh(gpu?.MemoryPercent)));

        string network = pending ? Pending
            : s!.NetworkDownBytesPerSecond is { } down && s.NetworkUpBytesPerSecond is { } up
                ? $"↓ {Units.Rate(down)}  ↑ {Units.Rate(up)}"
                : NotAvailable;
        rows.Add((JsonNode)Detail("Network", network));

        foreach (var name in options.Drives ?? [Sensors.DriveCatalog.SystemDriveName])
        {
            string label = $"Disk {name}";
            if (pending)
            {
                rows.Add((JsonNode)Detail(label, Pending));
            }
            else if (s!.Drives is null)
            {
                rows.Add((JsonNode)Detail(label, NotAvailable)); // disk sensor unavailable
            }
            else if (s.FindDrive(name) is { } drive)
            {
                double? percent = drive.Percent;
                rows.Add((JsonNode)Detail(label, FormatPercent(percent),
                    $"{FormatPercent(percent)} · {Units.UsedOfTotal(drive.UsedBytes, drive.TotalBytes)}",
                    percent, IsHigh(percent)));
            }
            // A selected drive that is not attached right now is simply left out.
        }

        string ram = pending ? Pending
            : s!.MemoryUsedBytes is { } mu && s.MemoryTotalBytes is { } mt ? Units.UsedOfTotal(mu, mt) : NotAvailable;
        rows.Add((JsonNode)Detail("Memory", ram, percent: s?.MemoryPercent, high: IsHigh(s?.MemoryPercent), largeOnly: true));

        return rows;
    }

    private static JsonObject Detail(string label, string value, string? valueLong = null, double? percent = null,
        bool high = false, bool largeOnly = false)
    {
        var row = new JsonObject
        {
            ["label"] = label,
            ["value"] = value,
            ["valueLong"] = valueLong ?? value,
            ["largeOnly"] = largeOnly,
        };
        AddEmphasis(row, high);
        AddBar(row, percent, high);
        return row;
    }

    /// <summary>
    /// Semantic colours: the host maps them to the light/dark theme itself. Weight changes too, so
    /// the highlight survives a monochrome rendering (lock screen).
    /// </summary>
    private static void AddEmphasis(JsonObject row, bool high)
    {
        row["color"] = high ? "Attention" : "Default";
        row["weight"] = high ? "Bolder" : "Default";
    }

    /// <summary>
    /// Usage bar drawn by the templates as two weighted columns. Weights never reach 0 because a
    /// zero-weight column is not laid out consistently by every renderer.
    /// </summary>
    private static void AddBar(JsonObject row, double? percent, bool high)
    {
        bool hasBar = percent is { } p && double.IsFinite(p);
        int used = hasBar ? (int)Math.Round(Math.Clamp(percent!.Value, 0, 100)) : 0;
        row["hasBar"] = hasBar;
        row["bar"] = Math.Max(used, 1);
        row["rest"] = Math.Max(100 - used, 1);
        row["barStyle"] = high ? "attention" : "accent";
    }

    public static bool IsHigh(double? percent) => percent is > HighUsageThreshold;

    public static string FormatPercent(double? percent) =>
        percent is { } p && double.IsFinite(p)
            ? Math.Round(Math.Clamp(p, 0, 100), MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%"
            : NotAvailable;
}
