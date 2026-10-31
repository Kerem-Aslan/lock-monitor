using System.Globalization;
using System.Text.Json.Nodes;

namespace LockMonitor.Rendering;

/// <summary>Builds the Adaptive Card data payload that the templates bind to.</summary>
internal static class CardData
{
    /// <summary>Usage above this percentage is highlighted.</summary>
    public const double HighUsageThreshold = 85.0;

    public const string NotAvailable = "N/A";

    public readonly record struct Metric(string Label, double? Percent);

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
        bool high = IsHigh(m.Percent);
        return new JsonObject
        {
            ["label"] = m.Label,
            ["value"] = FormatPercent(m.Percent),
            // Semantic colours: the host maps them to the light/dark theme itself.
            // Weight changes too, so the highlight survives a monochrome rendering (lock screen).
            ["color"] = high ? "Attention" : "Default",
            ["weight"] = high ? "Bolder" : "Default",
        };
    }

    public static bool IsHigh(double? percent) => percent is > HighUsageThreshold;

    public static string FormatPercent(double? percent) =>
        percent is { } p && double.IsFinite(p)
            ? Math.Round(Math.Clamp(p, 0, 100)).ToString("0", CultureInfo.InvariantCulture) + "%"
            : NotAvailable;
}
