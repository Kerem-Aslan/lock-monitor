using System.Text.Json.Nodes;
using Microsoft.Windows.Widgets;

namespace LockMonitor.Rendering;

/// <summary>
/// Loads one Adaptive Card template per widget size (Templates\small.json, medium.json, large.json)
/// and merges them into a single card. Each size's body is wrapped in a Container guarded by
/// <c>$when: ${$host.widgetSize=="..."}</c>, so the host can redraw immediately when the user
/// resizes the widget, before the provider sends new data.
/// </summary>
internal sealed class TemplateStore
{
    private static readonly (WidgetSize Size, string Name)[] Sizes =
    [
        (WidgetSize.Small, "small"),
        (WidgetSize.Medium, "medium"),
        (WidgetSize.Large, "large"),
    ];

    public string Composed { get; }

    public IReadOnlySet<WidgetSize> AvailableSizes { get; }

    private TemplateStore(string composed, IReadOnlySet<WidgetSize> sizes)
    {
        Composed = composed;
        AvailableSizes = sizes;
    }

    public static TemplateStore LoadFrom(string directory)
    {
        var sources = new Dictionary<WidgetSize, string>();
        foreach (var (size, name) in Sizes)
        {
            var path = Path.Combine(directory, name + ".json");
            if (File.Exists(path))
            {
                sources[size] = File.ReadAllText(path);
            }
        }
        return FromSources(sources);
    }

    public static TemplateStore FromSources(IReadOnlyDictionary<WidgetSize, string> sources)
    {
        if (sources.Count == 0)
        {
            throw new InvalidOperationException("No widget templates found.");
        }

        JsonObject? root = null;
        var body = new JsonArray();
        foreach (var (size, name) in Sizes)
        {
            if (!sources.TryGetValue(size, out var json))
            {
                continue;
            }

            var card = JsonNode.Parse(json)?.AsObject()
                ?? throw new InvalidOperationException($"Template '{name}' is empty.");

            // The first template provides the card-level properties (version, $schema, ...).
            root ??= (JsonObject)card.DeepClone();

            var items = card["body"]?.DeepClone() as JsonArray ?? [];
            body.Add((JsonNode)new JsonObject
            {
                ["type"] = "Container",
                ["$when"] = $"${{$host.widgetSize==\"{name}\"}}",
                ["items"] = items,
            });
        }

        root!["body"] = body;
        return new TemplateStore(root.ToJsonString(), sources.Keys.ToHashSet());
    }
}
