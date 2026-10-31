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

/// <summary>Builds the Adaptive Card data payload that the templates bind to.</summary>
public static class CardData
{
    /// <summary>Usage above this percentage is highlighted.</summary>
    public const double HighUsageThreshold = 85.0;

    /// <summary>Shown when a sensor cannot provide a value on this machine.</summary>
    public const string NotAvailable = "N/A";

    /// <summary>Shown before the first sample arrives.</summary>
    public const string Pending = "--";

    public readonly record struct Metric(string Label, double? Percent, bool IsPending = false);

    /// <summary>Card data for a snapshot; a null snapshot means "no reading yet".</summary>
    public static string Build(SystemSnapshot? snapshot, CardSize size)
    {
        bool pending = snapshot is null;
        // Phase 2: every size shows the small set; medium/large fields arrive in phase 3.
        _ = size;
        return Build(
        [
            new Metric("CPU", snapshot?.CpuPercent, pending),
            new Metric("RAM", snapshot?.MemoryPercent, pending),
            new Metric("GPU", snapshot?.GpuPercent, pending),
        ]);
    }

    public static string Build(IEnumerable<Metric> metrics)
    {
        var rows = new JsonArray();
        foreach (var m in metrics)
        {
            rows.Add((JsonNode)Row(m));
        }
        return new JsonObject { ["metrics"] = rows }.ToJsonString();
    }

    public static JsonObject Row(Metric m)
    {
        bool high = !m.IsPending && IsHigh(m.Percent);
        return new JsonObject
        {
            ["label"] = m.Label,
            ["value"] = m.IsPending ? Pending : FormatPercent(m.Percent),
            // Semantic colours: the host maps them to the light/dark theme itself.
            // Weight changes too, so the highlight survives a monochrome rendering (lock screen).
            ["color"] = high ? "Attention" : "Default",
            ["weight"] = high ? "Bolder" : "Default",
        };
    }

    public static bool IsHigh(double? percent) => percent is > HighUsageThreshold;

    public static string FormatPercent(double? percent) =>
        percent is { } p && double.IsFinite(p)
            ? Math.Round(Math.Clamp(p, 0, 100), MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%"
            : NotAvailable;
}
