using System.Text.Json.Nodes;
using LockMonitor.Rendering;
using LockMonitor.Sampling;
using LockMonitor.Sensors;
using Windows.System.Power;
using Xunit;

namespace LockMonitor.Tests;

public class ThermalZoneTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Converts_tenths_of_kelvin_and_kelvin()
    {
        Assert.Equal(60.05, ThermalZoneSensor.KelvinToCelsius(3332, tenths: true), 2);
        Assert.Equal(68.85, ThermalZoneSensor.KelvinToCelsius(342, tenths: false), 2);
    }

    [Theory]
    [InlineData(-273.15, false)] // 0 K placeholder
    [InlineData(27.85, true)]
    [InlineData(200.0, false)]
    public void Rejects_implausible_values(double celsius, bool plausible) =>
        Assert.Equal(plausible, ThermalZoneSensor.IsPlausible(celsius));

    [Fact]
    public void A_value_that_never_changes_for_5_minutes_is_a_placeholder()
    {
        var detector = new StaticValueDetector(TimeSpan.FromMinutes(5));
        Assert.False(detector.IsStatic(27.85, T0));
        Assert.False(detector.IsStatic(27.85, T0.AddMinutes(4)));
        Assert.True(detector.IsStatic(27.85, T0.AddMinutes(5)));
    }

    [Fact]
    public void A_live_value_is_never_hidden()
    {
        var detector = new StaticValueDetector(TimeSpan.FromMinutes(5));
        detector.IsStatic(64.9, T0);
        Assert.False(detector.IsStatic(68.9, T0.AddSeconds(10)));
        Assert.False(detector.IsStatic(64.9, T0.AddMinutes(30))); // changed once: live for good
    }
}

public class BatteryTests
{
    [Fact]
    public void No_battery_means_no_reading()
    {
        Assert.Null(BatterySensor.Read(BatteryStatus.NotPresent, PowerSupplyStatus.Adequate, 0, TimeSpan.MaxValue));
    }

    [Fact]
    public void Maps_charge_states_and_keeps_only_real_estimates()
    {
        var charging = BatterySensor.Read(BatteryStatus.Charging, PowerSupplyStatus.Adequate, 76, TimeSpan.MaxValue)!;
        Assert.Equal(ChargeState.Charging, charging.State);
        Assert.Null(charging.TimeRemaining);

        var full = BatterySensor.Read(BatteryStatus.Idle, PowerSupplyStatus.Adequate, 100, TimeSpan.MaxValue)!;
        Assert.Equal(ChargeState.PluggedIn, full.State);

        var onBattery = BatterySensor.Read(BatteryStatus.Discharging, PowerSupplyStatus.NotPresent, 54, TimeSpan.FromMinutes(130))!;
        Assert.Equal(ChargeState.Discharging, onBattery.State);
        Assert.Equal(TimeSpan.FromMinutes(130), onBattery.TimeRemaining);

        var noEstimate = BatterySensor.Read(BatteryStatus.Discharging, PowerSupplyStatus.NotPresent, 54, TimeSpan.MaxValue)!;
        Assert.Null(noEstimate.TimeRemaining);
    }

    [Theory]
    [InlineData(ChargeState.Charging, 76, null, "76% · Charging")]
    [InlineData(ChargeState.PluggedIn, 100, null, "100% · Plugged in")]
    [InlineData(ChargeState.Discharging, 54, 130, "54% · 2 h 10 min left")]
    [InlineData(ChargeState.Discharging, 54, null, "54% · On battery")]
    public void Formats_battery(ChargeState state, int percent, int? minutesLeft, string expected)
    {
        var reading = new BatteryReading(percent, state, minutesLeft is { } m ? TimeSpan.FromMinutes(m) : null);
        Assert.Equal(expected, CardData.FormatBattery(reading));
    }
}

public class ProcessCpuTrackerTests
{
    private const long Second = 10_000_000; // 100 ns units

    [Fact]
    public void First_sample_is_a_baseline_then_apps_are_grouped_by_name_and_normalized()
    {
        var tracker = new ProcessCpuTracker(logicalProcessors: 4);
        tracker.BeginSample(timestamp: 0, frequency: 1);
        tracker.Add(10, 1, 0, "chrome.exe");
        tracker.Add(11, 1, 0, "Chrome.exe");
        tracker.Add(20, 1, 0, "devenv.exe");
        tracker.Add(30, 1, 0, "Idle-ish.exe");
        Assert.Null(tracker.EndSample(3));

        tracker.BeginSample(timestamp: 2, frequency: 1); // 2 s later, capacity = 2 s * 4 CPUs
        tracker.Add(10, 1, 2 * Second, "chrome.exe");   // 2 s of CPU
        tracker.Add(11, 1, 2 * Second, "Chrome.exe");   // + 2 s, same app
        tracker.Add(20, 1, 1 * Second, "devenv.exe");   // 1 s
        tracker.Add(30, 1, 0, "Idle-ish.exe");          // nothing
        var top = tracker.EndSample(3)!;

        Assert.Equal(2, top.Count);
        Assert.Equal("chrome", top[0].Name);
        Assert.Equal(50, top[0].CpuPercent, 3); // 4 s / 8 s
        Assert.Equal("devenv", top[1].Name);
        Assert.Equal(12.5, top[1].CpuPercent, 3);
    }

    [Fact]
    public void A_reused_process_id_is_not_mistaken_for_the_old_process()
    {
        var tracker = new ProcessCpuTracker(1);
        tracker.BeginSample(0, 1);
        tracker.Add(10, createTime: 1, 5 * Second, "old.exe");
        tracker.EndSample(3);

        tracker.BeginSample(1, 1);
        tracker.Add(10, createTime: 2, 9 * Second, "new.exe"); // same PID, new process
        Assert.Empty(tracker.EndSample(3)!);
    }
}

public class Phase4CardTests
{
    [Theory]
    [InlineData(30, "<1 min")]
    [InlineData(14 * 60, "14 min")]
    [InlineData(5 * 3600 + 12 * 60, "5 h 12 min")]
    [InlineData(3 * 86400 + 4 * 3600, "3 d 4 h")]
    public void Durations(int seconds, string expected) =>
        Assert.Equal(expected, Units.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Large_card_shows_system_temp_battery_uptime_and_top_processes()
    {
        var snapshot = new SystemSnapshot
        {
            CpuPercent = 10,
            SystemTemperatureCelsius = 68.9,
            Battery = new BatteryReading(12, ChargeState.Discharging, TimeSpan.FromMinutes(40)),
            Uptime = TimeSpan.FromHours(57),
            TopProcesses = [new ProcessUsage("chrome", 12.4), new ProcessUsage("devenv", 3)],
            Drives = [],
        };
        var data = JsonNode.Parse(CardData.Build(snapshot, CardSize.Large))!;
        var details = data["details"]!.AsArray().ToDictionary(r => (string)r!["label"]!, r => r!);

        Assert.Equal("69 °C", (string?)details["System temp (ACPI)"]["value"]);
        Assert.Equal("12% · 40 min left", (string?)details["Battery"]["valueLong"]);
        Assert.Equal("Attention", (string?)details["Battery"]["color"]); // low while discharging
        Assert.Equal("2 d 9 h", (string?)details["Uptime"]["value"]);
        Assert.All(new[] { "System temp (ACPI)", "Battery", "Uptime" }, k => Assert.True((bool)details[k]["largeOnly"]!));

        var top = data["topProcesses"]!.AsArray();
        Assert.Equal(["chrome", "devenv"], top.Select(p => (string?)p!["name"]));
        Assert.Equal("12%", (string?)top[0]!["value"]);
    }

    [Fact]
    public void Missing_battery_and_thermal_zone_rows_are_hidden_and_medium_has_no_process_list()
    {
        var snapshot = new SystemSnapshot { Drives = [], TopProcesses = [new ProcessUsage("x", 1)] };
        var large = JsonNode.Parse(CardData.Build(snapshot, CardSize.Large))!;
        var labels = large["details"]!.AsArray().Select(r => (string?)r!["label"]).ToList();
        Assert.DoesNotContain("Battery", labels);
        Assert.DoesNotContain("System temp (ACPI)", labels);

        var medium = JsonNode.Parse(CardData.Build(snapshot, CardSize.Medium))!;
        Assert.Empty(medium["topProcesses"]!.AsArray()); // present but empty: templates can count() it
    }
}
