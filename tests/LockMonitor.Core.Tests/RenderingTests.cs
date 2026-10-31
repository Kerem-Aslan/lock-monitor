using System.Text.Json.Nodes;
using LockMonitor.Rendering;
using LockMonitor.Sampling;
using Xunit;

namespace LockMonitor.Tests;

public class CardDataTests
{
    [Theory]
    [InlineData(0.0, "0%")]
    [InlineData(12.4, "12%")]
    [InlineData(12.5, "13%")]
    [InlineData(99.6, "100%")]
    [InlineData(131.0, "100%")]
    [InlineData(-3.0, "0%")]
    [InlineData(double.NaN, "N/A")]
    public void Formats_percentages(double value, string expected)
    {
        Assert.Equal(expected, CardData.FormatPercent(value));
    }

    [Fact]
    public void Missing_value_is_not_available()
    {
        Assert.Equal(CardData.NotAvailable, CardData.FormatPercent(null));
    }

    [Theory]
    [InlineData(85.0, false)]
    [InlineData(85.01, true)]
    [InlineData(100.0, true)]
    [InlineData(null, false)]
    public void Highlights_only_above_85_percent(double? value, bool high)
    {
        Assert.Equal(high, CardData.IsHigh(value));
        var row = CardData.Row(new CardData.Metric("CPU", value));
        Assert.Equal(high ? "Attention" : "Default", (string?)row["color"]);
        Assert.Equal(high ? "Bolder" : "Default", (string?)row["weight"]);
    }

    [Fact]
    public void No_snapshot_yet_shows_pending_placeholders_without_highlight()
    {
        var data = JsonNode.Parse(CardData.Build(null, CardSize.Small))!;
        var rows = data["metrics"]!.AsArray();
        Assert.Equal(["CPU", "RAM", "GPU"], rows.Select(r => (string?)r!["label"]));
        Assert.All(rows, r =>
        {
            Assert.Equal(CardData.Pending, (string?)r!["value"]);
            Assert.Equal("Default", (string?)r["color"]);
        });
    }

    [Fact]
    public void Snapshot_values_are_formatted_and_failed_sensors_show_NA()
    {
        var snapshot = new SystemSnapshot { CpuPercent = 23.2, MemoryPercent = 47.8, GpuPercent = null };
        var rows = JsonNode.Parse(CardData.Build(snapshot, CardSize.Small))!["metrics"]!.AsArray();
        Assert.Equal(["23%", "48%", "N/A"], rows.Select(r => (string?)r!["value"]));
    }
}

public class TemplateStoreTests
{
    [Fact]
    public void Each_size_is_wrapped_in_a_container_guarded_by_host_widget_size()
    {
        const string card = """{ "type": "AdaptiveCard", "version": "1.5", "body": [ { "type": "TextBlock", "text": "x" } ] }""";
        var store = TemplateStore.FromSources(new Dictionary<CardSize, string>
        {
            [CardSize.Small] = card,
            [CardSize.Large] = card,
        });

        var root = JsonNode.Parse(store.Composed)!;
        Assert.Equal("1.5", (string?)root["version"]);
        var body = root["body"]!.AsArray();
        Assert.Equal(2, body.Count);
        Assert.Equal("""${$host.widgetSize=="small"}""", (string?)body[0]!["$when"]);
        Assert.Equal("""${$host.widgetSize=="large"}""", (string?)body[1]!["$when"]);
        Assert.Equal("TextBlock", (string?)body[0]!["items"]![0]!["type"]);
        Assert.Equal([CardSize.Small, CardSize.Large], store.AvailableSizes.Order());
    }

    [Fact]
    public void Shipped_templates_load_and_parse()
    {
        var store = TemplateStore.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Templates"));
        Assert.Contains(CardSize.Small, store.AvailableSizes);
        Assert.NotNull(JsonNode.Parse(store.Composed));
    }

    [Fact]
    public void Missing_templates_are_an_error()
    {
        Assert.Throws<InvalidOperationException>(() => TemplateStore.FromSources(new Dictionary<CardSize, string>()));
    }
}
