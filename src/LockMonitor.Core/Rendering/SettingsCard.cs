using System.Globalization;
using System.Text.Json.Nodes;
using LockMonitor.Native;
using LockMonitor.Sampling;

namespace LockMonitor.Rendering;

/// <summary>Data for Templates\settings.json (the "Customize widget" card).</summary>
public static class SettingsCard
{
    public const string HybridNote =
        "Off by default on PCs with two graphics cards: reading the temperature can keep the discrete GPU awake and drain the battery.";

    public static string BuildData(WidgetSettings settings, IReadOnlyList<GpuAdapterInfo> adapters, GpuAdapterInfo? defaultAdapter,
        IReadOnlyList<DriveReading> drives, string systemDrive)
    {
        bool hybrid = adapters.Count >= 2;
        var gpuChoices = new JsonArray
        {
            (JsonNode)new JsonObject
            {
                ["title"] = defaultAdapter is null ? "Automatic" : $"Automatic ({defaultAdapter.Name})",
                ["value"] = WidgetSettings.AutomaticGpu,
            },
        };
        foreach (var adapter in adapters)
        {
            gpuChoices.Add((JsonNode)new JsonObject { ["title"] = adapter.Name, ["value"] = adapter.Name });
        }

        var driveChoices = new JsonArray();
        foreach (var drive in drives)
        {
            driveChoices.Add((JsonNode)new JsonObject { ["title"] = DriveTitle(drive), ["value"] = drive.Name });
        }

        bool knownGpu = settings.GpuName is { } name && adapters.Any(a => a.Name == name);
        return new JsonObject
        {
            ["interval"] = settings.IntervalSeconds.ToString(CultureInfo.InvariantCulture),
            ["showGpuTemp"] = settings.EffectiveShowGpuTemperature(hybrid) ? "true" : "false",
            ["gpuTempNote"] = hybrid ? HybridNote : "",
            ["showGpuPicker"] = hybrid,
            ["gpu"] = knownGpu ? settings.GpuName : WidgetSettings.AutomaticGpu,
            ["gpuChoices"] = gpuChoices,
            // Multi-select ChoiceSet value: comma-separated drive names.
            ["drives"] = string.Join(",", settings.SelectedDrives(systemDrive)),
            ["driveChoices"] = driveChoices,
            ["showDrivePicker"] = drives.Count > 0,
        }.ToJsonString();
    }

    /// <summary>"C: (Windows)", "D: NOIDRIVE", "E:".</summary>
    public static string DriveTitle(DriveReading drive) =>
        drive.IsSystem ? $"{drive.Name} (Windows)"
        : drive.Label.Length > 0 ? $"{drive.Name} {drive.Label}"
        : drive.Name;
}
