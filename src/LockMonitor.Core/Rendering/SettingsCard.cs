using System.Globalization;
using System.Text.Json.Nodes;
using LockMonitor.Native;

namespace LockMonitor.Rendering;

/// <summary>Data for Templates\settings.json (the "Customize widget" card).</summary>
public static class SettingsCard
{
    public const string HybridNote =
        "Off by default on PCs with two graphics cards: reading the temperature can keep the discrete GPU awake and drain the battery.";

    public static string BuildData(WidgetSettings settings, IReadOnlyList<GpuAdapterInfo> adapters, GpuAdapterInfo? defaultAdapter)
    {
        bool hybrid = adapters.Count >= 2;
        var choices = new JsonArray
        {
            (JsonNode)new JsonObject
            {
                ["title"] = defaultAdapter is null ? "Automatic" : $"Automatic ({defaultAdapter.Name})",
                ["value"] = WidgetSettings.AutomaticGpu,
            },
        };
        foreach (var adapter in adapters)
        {
            choices.Add((JsonNode)new JsonObject { ["title"] = adapter.Name, ["value"] = adapter.Name });
        }

        bool knownGpu = settings.GpuName is { } name && adapters.Any(a => a.Name == name);
        return new JsonObject
        {
            ["interval"] = settings.IntervalSeconds.ToString(CultureInfo.InvariantCulture),
            ["showGpuTemp"] = settings.EffectiveShowGpuTemperature(hybrid) ? "true" : "false",
            ["gpuTempNote"] = hybrid ? HybridNote : "",
            ["showGpuPicker"] = hybrid,
            ["gpu"] = knownGpu ? settings.GpuName : WidgetSettings.AutomaticGpu,
            ["gpuChoices"] = choices,
        }.ToJsonString();
    }
}
