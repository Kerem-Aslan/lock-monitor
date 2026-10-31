using System.Text.Json.Nodes;
using LockMonitor.Native;
using LockMonitor.Rendering;
using LockMonitor.Sampling;
using Xunit;

namespace LockMonitor.Tests;

public class CardDataTests
{
    private const long Nvidia = 0x11D3C;
    private const long Intel = 0x2222;

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
        Assert.Equal(high ? "attention" : "accent", (string?)row["barStyle"]);
    }

    [Theory]
    [InlineData(0.0, 1, 100)]
    [InlineData(23.4, 23, 77)]
    [InlineData(100.0, 100, 1)]
    public void Bars_use_non_zero_column_weights(double percent, int bar, int rest)
    {
        var row = CardData.Row(new CardData.Metric("CPU", percent));
        Assert.True((bool)row["hasBar"]!);
        Assert.Equal(bar, (int)row["bar"]!);
        Assert.Equal(rest, (int)row["rest"]!);
    }

    [Fact]
    public void No_snapshot_yet_shows_pending_placeholders_without_highlight()
    {
        var data = JsonNode.Parse(CardData.Build(null, CardSize.Medium))!;
        var rows = data["metrics"]!.AsArray();
        Assert.Equal(["CPU", "RAM", "GPU"], rows.Select(r => (string?)r!["label"]));
        Assert.All(rows, r =>
        {
            Assert.Equal(CardData.Pending, (string?)r!["value"]);
            Assert.Equal("Default", (string?)r["color"]);
            Assert.False((bool)r["hasBar"]!);
        });
        Assert.All(data["details"]!.AsArray(), r => Assert.Equal(CardData.Pending, (string?)r!["value"]));
    }

    [Fact]
    public void Small_card_has_no_detail_rows_and_failed_sensors_show_NA()
    {
        var snapshot = new SystemSnapshot { CpuPercent = 23.2, MemoryPercent = 47.8 };
        var data = JsonNode.Parse(CardData.Build(snapshot, CardSize.Small))!;
        Assert.Equal(["23%", "48%", "N/A"], data["metrics"]!.AsArray().Select(r => (string?)r!["value"]));
        Assert.Null(data["details"]);
    }

    [Fact]
    public void Medium_card_details_cover_temperature_vram_network_and_disk()
    {
        var data = JsonNode.Parse(CardData.Build(Sample(), CardSize.Medium))!;
        var details = data["details"]!.AsArray().ToDictionary(r => (string)r!["label"]!, r => r!);

        Assert.Equal("72 °C", (string?)details["GPU temp"]["value"]);
        Assert.Equal("2.9 / 8.0 GB", (string?)details["VRAM"]["value"]);
        Assert.Equal("↓ 1.5 MB/s  ↑ 80 KB/s", (string?)details["Network"]["value"]);
        Assert.Equal("62%", (string?)details["Disk C:"]["value"]);
        Assert.Equal("62% · 310.0 / 500.0 GB", (string?)details["Disk C:"]["valueLong"]);
        Assert.True((bool)details["Memory"]["largeOnly"]!);
        Assert.Equal("NVIDIA GeForce RTX 4060 Laptop GPU", (string?)data["gpuName"]);
    }

    [Fact]
    public void Gpu_temperature_row_follows_the_setting_and_highlights_at_85_degrees()
    {
        var snapshot = Sample();
        snapshot.FindGpu(Nvidia)!.TemperatureCelsius = 86;

        var shown = Details(CardData.Build(snapshot, CardSize.Medium, new CardOptions(null, ShowGpuTemperature: true)));
        Assert.Equal("Attention", (string?)shown["GPU temp"]["color"]);

        var hidden = Details(CardData.Build(snapshot, CardSize.Medium, new CardOptions(null, ShowGpuTemperature: false)));
        Assert.False(hidden.ContainsKey("GPU temp"));
    }

    [Fact]
    public void Widget_can_show_a_non_default_gpu_and_integrated_gpus_report_shared_memory()
    {
        var data = JsonNode.Parse(CardData.Build(Sample(), CardSize.Medium, new CardOptions(Intel, true)))!;
        Assert.Equal("Intel(R) Graphics", (string?)data["gpuName"]);
        Assert.Equal("5%", (string?)data["metrics"]![2]!["value"]);
        var details = Details(data.ToJsonString());
        Assert.True(details.ContainsKey("GPU memory"));
        Assert.Equal(CardData.NotAvailable, (string?)details["GPU temp"]["value"]);
    }

    [Fact]
    public void Each_selected_drive_gets_a_row_missing_drives_are_skipped()
    {
        var details = Details(CardData.Build(Sample(), CardSize.Large, new CardOptions(Drives: ["C:", "D:", "E:"])));
        Assert.Equal("62%", (string?)details["Disk C:"]["value"]);
        Assert.Equal("90% · 900.0 / 1000.0 GB", (string?)details["Disk D:"]["valueLong"]);
        Assert.Equal("Attention", (string?)details["Disk D:"]["color"]);
        Assert.False(details.ContainsKey("Disk E:")); // not attached
    }

    [Fact]
    public void No_selected_drive_means_no_disk_rows_and_a_failed_disk_sensor_shows_NA()
    {
        var none = Details(CardData.Build(Sample(), CardSize.Medium, new CardOptions(Drives: [])));
        Assert.DoesNotContain(none.Keys, k => k.StartsWith("Disk", StringComparison.Ordinal));

        var snapshot = Sample();
        snapshot.Drives = null;
        var failed = Details(CardData.Build(snapshot, CardSize.Medium, new CardOptions(Drives: ["D:"])));
        Assert.Equal(CardData.NotAvailable, (string?)failed["Disk D:"]["value"]);
    }

    private static Dictionary<string, JsonNode> Details(string json) =>
        JsonNode.Parse(json)!["details"]!.AsArray().ToDictionary(r => (string)r!["label"]!, r => r!);

    private static SystemSnapshot Sample()
    {
        var s = new SystemSnapshot
        {
            CpuPercent = 23,
            MemoryPercent = 50,
            MemoryUsedBytes = 8UL << 30,
            MemoryTotalBytes = 16UL << 30,
            DefaultGpuLuid = Nvidia,
            NetworkDownBytesPerSecond = 1.5 * 1024 * 1024,
            NetworkUpBytesPerSecond = 80 * 1024,
            Drives =
            [
                new DriveReading("C:", "", true, 310UL << 30, 500UL << 30),
                new DriveReading("D:", "NOIDRIVE", false, 900UL << 30, 1000UL << 30),
            ],
        };
        var nvidia = s.GetOrAddGpu(Nvidia, "NVIDIA GeForce RTX 4060 Laptop GPU");
        nvidia.UsagePercent = 37;
        nvidia.MemoryUsedBytes = (ulong)(2.9 * (1 << 30));
        nvidia.MemoryTotalBytes = 8UL << 30;
        nvidia.TemperatureCelsius = 72;
        var intel = s.GetOrAddGpu(Intel, "Intel(R) Graphics");
        intel.UsagePercent = 5;
        intel.MemoryIsShared = true;
        intel.MemoryUsedBytes = 1UL << 30;
        intel.MemoryTotalBytes = 8UL << 30;
        return s;
    }
}

public class TemplateStoreTests
{
    [Fact]
    public void Each_size_gets_a_board_variant_with_tap_action_and_a_lock_screen_variant_without()
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
        Assert.Equal(4, body.Count);

        Assert.Equal("""${$host.widgetSize=="small" && $host.isUserContextAuthenticated != false && exists(metrics)}""", (string?)body[0]!["$when"]);
        Assert.Equal(TemplateStore.OpenTaskManagerVerb, (string?)body[0]!["selectAction"]!["verb"]);
        Assert.Equal("""${$host.widgetSize=="small" && $host.isUserContextAuthenticated == false && exists(metrics)}""", (string?)body[1]!["$when"]);
        Assert.Null(body[1]!["selectAction"]);
        Assert.StartsWith("""${$host.widgetSize=="large" """.TrimEnd(), (string?)body[2]!["$when"]);
        Assert.Equal("TextBlock", (string?)body[0]!["items"]![0]!["type"]);
        Assert.Equal([CardSize.Small, CardSize.Large], store.AvailableSizes.Order());
    }

    [Fact]
    public void Shipped_templates_load_for_all_sizes_and_the_settings_card()
    {
        var store = TemplateStore.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Templates"));
        Assert.Equal([CardSize.Small, CardSize.Medium, CardSize.Large], store.AvailableSizes.Order());
        Assert.NotNull(JsonNode.Parse(store.Composed));
        Assert.NotNull(JsonNode.Parse(store.Settings!));
    }

    [Fact]
    public void Missing_templates_are_an_error()
    {
        Assert.Throws<InvalidOperationException>(() => TemplateStore.FromSources(new Dictionary<CardSize, string>()));
    }
}

public class SettingsTests
{
    private static readonly GpuAdapterInfo Nvidia = new(1, "NVIDIA GeForce RTX 4060 Laptop GPU", 0x10DE, 8UL << 30, false);
    private static readonly GpuAdapterInfo Intel = new(2, "Intel(R) Graphics", 0x8086, 128UL << 20, false);

    private static readonly DriveReading[] Drives =
    [
        new("C:", "", true, 1, 2),
        new("D:", "NOIDRIVE", false, 1, 2),
        new("E:", "", false, 1, 2),
    ];

    [Fact]
    public void Drive_selection_defaults_to_the_system_drive_and_is_normalized()
    {
        Assert.Equal(["C:"], WidgetSettings.Default.SelectedDrives("C:"));
        Assert.Equal("C:,D:,E:", WidgetSettings.NormalizeDrives(@"d:, c:,C:,x,E:\"));

        var both = WidgetSettings.Default.WithInputs("""{"drives":"D:,C:"}""");
        Assert.Equal("C:,D:", both.DriveList);
        Assert.Equal(["C:", "D:"], both.SelectedDrives("C:"));
        Assert.Equal(both, WidgetSettings.Parse(both.ToJson()));

        var none = both.WithInputs("""{"drives":""}"""); // every box unticked
        Assert.Empty(none.SelectedDrives("C:"));
        Assert.Empty(WidgetSettings.Parse(none.ToJson()).SelectedDrives("C:"));
    }

    [Fact]
    public void Settings_card_lists_drives_with_labels()
    {
        var data = JsonNode.Parse(SettingsCard.BuildData(WidgetSettings.Default, [Nvidia], Nvidia, Drives, "C:"))!;
        Assert.Equal(["C: (Windows)", "D: NOIDRIVE", "E:"], data["driveChoices"]!.AsArray().Select(c => (string?)c!["title"]));
        Assert.Equal("C:", (string?)data["drives"]);
        Assert.True((bool)data["showDrivePicker"]!);
    }

    [Fact]
    public void Defaults_and_round_trip()
    {
        var defaults = WidgetSettings.Parse(null);
        Assert.Equal(2, defaults.IntervalSeconds);
        Assert.Null(defaults.ShowGpuTemperature);
        Assert.Null(defaults.GpuName);

        var custom = new WidgetSettings { IntervalSeconds = 5, ShowGpuTemperature = false, GpuName = "Intel(R) Graphics" };
        Assert.Equal(custom, WidgetSettings.Parse(custom.ToJson()));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"interval": 7}""")]
    [InlineData("""{"interval": "abc", "gpuTemp": "maybe"}""")]
    public void Malformed_or_unsupported_values_fall_back_to_defaults(string customState)
    {
        var settings = WidgetSettings.Parse(customState);
        Assert.Equal(WidgetSettings.DefaultIntervalSeconds, settings.IntervalSeconds);
        Assert.Null(settings.ShowGpuTemperature);
    }

    [Fact]
    public void Settings_card_inputs_arrive_as_strings()
    {
        var updated = WidgetSettings.Default.WithInputs("""{"interval":"10","showGpuTemp":"true","gpu":"auto"}""");
        Assert.Equal(10, updated.IntervalSeconds);
        Assert.True(updated.ShowGpuTemperature);
        Assert.Null(updated.GpuName);

        updated = updated.WithInputs("""{"gpu":"Intel(R) Graphics","showGpuTemp":"false"}""");
        Assert.Equal("Intel(R) Graphics", updated.GpuName);
        Assert.False(updated.ShowGpuTemperature);
        Assert.Equal(10, updated.IntervalSeconds); // untouched
    }

    [Fact]
    public void Gpu_temperature_defaults_off_only_on_hybrid_machines()
    {
        Assert.True(WidgetSettings.Default.EffectiveShowGpuTemperature(isHybrid: false));
        Assert.False(WidgetSettings.Default.EffectiveShowGpuTemperature(isHybrid: true));
        Assert.True((WidgetSettings.Default with { ShowGpuTemperature = true }).EffectiveShowGpuTemperature(isHybrid: true));
    }

    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(1, true, 5)]
    [InlineData(2, true, 5)]
    [InlineData(10, true, 10)]
    public void Energy_saver_slows_updates_to_at_most_every_5_seconds(int seconds, bool saver, int expected)
    {
        var settings = WidgetSettings.Default with { IntervalSeconds = seconds };
        Assert.Equal(TimeSpan.FromSeconds(expected), settings.EffectiveInterval(saver));
    }

    [Fact]
    public void Settings_card_offers_a_gpu_picker_and_a_note_only_on_hybrid_machines()
    {
        var single = JsonNode.Parse(SettingsCard.BuildData(WidgetSettings.Default, [Nvidia], Nvidia, Drives, "C:"))!;
        Assert.False((bool)single["showGpuPicker"]!);
        Assert.Equal("true", (string?)single["showGpuTemp"]);
        Assert.Equal("", (string?)single["gpuTempNote"]);

        var hybrid = JsonNode.Parse(SettingsCard.BuildData(WidgetSettings.Default, [Nvidia, Intel], Nvidia, Drives, "C:"))!;
        Assert.True((bool)hybrid["showGpuPicker"]!);
        Assert.Equal("false", (string?)hybrid["showGpuTemp"]);
        Assert.Equal(SettingsCard.HybridNote, (string?)hybrid["gpuTempNote"]);
        Assert.Equal(3, hybrid["gpuChoices"]!.AsArray().Count);
        Assert.Equal("auto", (string?)hybrid["gpu"]);
    }
}

public class UnitsTests
{
    [Theory]
    [InlineData(0.0, "0 KB/s")]
    [InlineData(80 * 1024.0, "80 KB/s")]
    [InlineData(1.5 * 1024 * 1024, "1.5 MB/s")]
    [InlineData(48.0 * 1024 * 1024, "48 MB/s")]
    [InlineData(-5.0, "0 KB/s")]
    public void Rates(double bytesPerSecond, string expected) => Assert.Equal(expected, Units.Rate(bytesPerSecond));

    [Theory]
    [InlineData(512UL << 20, 768UL << 20, "512 / 768 MB")]
    [InlineData(3UL << 30, 8UL << 30, "3.0 / 8.0 GB")]
    [InlineData(1UL << 40, 2UL << 40, "1.0 / 2.0 TB")]
    public void Used_of_total(ulong used, ulong total, string expected) => Assert.Equal(expected, Units.UsedOfTotal(used, total));

    [Fact]
    public void Temperature() => Assert.Equal("42 °C", Units.Celsius(41.6));
}
