using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LockMonitor.Rendering;

/// <summary>
/// Per-widget user settings. Stored by the widget host as the widget's CustomState (JSON), so
/// they survive provider restarts and every pinned widget can have its own.
/// </summary>
public sealed record WidgetSettings
{
    public const int DefaultIntervalSeconds = 2;
    public static readonly int[] IntervalChoices = [1, 2, 5, 10];

    /// <summary>Interval used while Windows battery/energy saver is on.</summary>
    public static readonly TimeSpan EnergySaverMinInterval = TimeSpan.FromSeconds(5);

    public const string AutomaticGpu = "auto";

    public int IntervalSeconds { get; init; } = DefaultIntervalSeconds;

    /// <summary>null = default: on, except on hybrid-graphics machines (see GpuAdapterCatalog.IsHybrid).</summary>
    public bool? ShowGpuTemperature { get; init; }

    /// <summary>Adapter name (DXGI description), or null for the automatic choice.</summary>
    public string? GpuName { get; init; }

    public static WidgetSettings Default { get; } = new();

    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);

    public TimeSpan EffectiveInterval(bool energySaverOn) =>
        energySaverOn && Interval < EnergySaverMinInterval ? EnergySaverMinInterval : Interval;

    public bool EffectiveShowGpuTemperature(bool isHybrid) => ShowGpuTemperature ?? !isHybrid;

    public string ToJson()
    {
        var json = new JsonObject { ["v"] = 1, ["interval"] = IntervalSeconds };
        if (ShowGpuTemperature is { } show)
        {
            json["gpuTemp"] = show;
        }
        if (GpuName is not null)
        {
            json["gpu"] = GpuName;
        }
        return json.ToJsonString();
    }

    /// <summary>Reads CustomState; anything missing or malformed falls back to the defaults.</summary>
    public static WidgetSettings Parse(string? customState)
    {
        if (string.IsNullOrWhiteSpace(customState))
        {
            return Default;
        }
        try
        {
            if (JsonNode.Parse(customState) is not JsonObject json)
            {
                return Default;
            }
            return new WidgetSettings
            {
                IntervalSeconds = NormalizeInterval(ReadInt(json["interval"])),
                ShowGpuTemperature = ReadBool(json["gpuTemp"]),
                GpuName = ReadString(json["gpu"]) is { Length: > 0 } gpu && gpu != AutomaticGpu ? gpu : null,
            };
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    /// <summary>
    /// Applies the values submitted from the settings card (Action.Execute data: every input is a
    /// string). The temperature toggle is stored explicitly once the user saves, so later changes of
    /// the default do not flip it.
    /// </summary>
    public WidgetSettings WithInputs(string? actionData)
    {
        if (string.IsNullOrWhiteSpace(actionData))
        {
            return this;
        }
        try
        {
            if (JsonNode.Parse(actionData) is not JsonObject json)
            {
                return this;
            }
            var result = this;
            if (ReadInt(json["interval"]) is { } interval)
            {
                result = result with { IntervalSeconds = NormalizeInterval(interval) };
            }
            if (ReadBool(json["showGpuTemp"]) is { } show)
            {
                result = result with { ShowGpuTemperature = show };
            }
            if (ReadString(json["gpu"]) is { } gpu)
            {
                result = result with { GpuName = gpu.Length == 0 || gpu == AutomaticGpu ? null : gpu };
            }
            return result;
        }
        catch (JsonException)
        {
            return this;
        }
    }

    private static int NormalizeInterval(int? seconds) =>
        seconds is { } s && IntervalChoices.Contains(s) ? s : DefaultIntervalSeconds;

    private static int? ReadInt(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out int i) => i,
        JsonValue v when v.TryGetValue(out string? s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) => p,
        _ => null,
    };

    private static bool? ReadBool(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out bool b) => b,
        JsonValue v when v.TryGetValue(out string? s) && bool.TryParse(s, out bool p) => p,
        _ => null,
    };

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) ? s : null;
}
