using System.Runtime.CompilerServices;
using LockMonitor.Native;
using LockMonitor.Rendering;
using LockMonitor.Sampling;
using LockMonitor.Sensors;
using Xunit;

namespace LockMonitor.Tests;

public class NativeLayoutTests
{
    [Fact]
    public void MIB_IF_ROW2_matches_the_native_size()
    {
        Assert.Equal(IpHlpApi.ExpectedRowSize, Unsafe.SizeOf<IpHlpApi.MIB_IF_ROW2>());
    }

    [Fact]
    public void D3DKMT_ADAPTER_PERFDATA_matches_the_native_size()
    {
        Assert.Equal(64, Unsafe.SizeOf<D3dkmt.D3DKMT_ADAPTER_PERFDATA>());
    }

    [Theory]
    [InlineData(0x01, 1, 6, true)]   // hardware ethernet, up
    [InlineData(0x01, 2, 6, false)]  // down
    [InlineData(0x03, 1, 6, false)]  // NDIS filter instance (WFP/QoS) would double count
    [InlineData(0x00, 1, 6, false)]  // virtual adapter (Hyper-V vEthernet)
    [InlineData(0x01, 1, 24, false)] // loopback
    [InlineData(0x01, 1, 131, false)]// tunnel / VPN
    public void Only_physical_connected_interfaces_count(byte flags, int operStatus, int type, bool expected)
    {
        var row = new IpHlpApi.MIB_IF_ROW2 { InterfaceAndOperStatusFlags = flags, OperStatus = operStatus, Type = type };
        Assert.Equal(expected, IpHlpApi.IsCountable(row));
    }
}

public class ByteRateCounterTests
{
    [Fact]
    public void First_reading_is_the_baseline_then_rates_follow()
    {
        var counter = new ByteRateCounter();
        Assert.Null(counter.Update(1_000, 0, 1000));
        Assert.Equal(500, counter.Update(2_000, 2000, 1000)); // 1000 bytes in 2 s
    }

    [Fact]
    public void Counter_going_backwards_restarts_the_baseline()
    {
        var counter = new ByteRateCounter();
        counter.Update(5_000, 0, 1000);
        Assert.Null(counter.Update(100, 1000, 1000));
        Assert.Equal(900, counter.Update(1_000, 2000, 1000));
    }
}

public class GpuMemoryParsingTests
{
    [Theory]
    [InlineData("luid_0x00000000_0x00011D3C_phys_0", 0x11D3CL)]
    [InlineData("luid_0x00000001_0x0000f00d_phys_1", 0x1_0000F00DL)]
    public void Parses_adapter_memory_instances(string name, long expected)
    {
        Assert.True(GpuMemorySensor.TryParseLuid(name, out long luid));
        Assert.Equal(expected, luid);
    }

    [Fact]
    public void Rejects_other_instances()
    {
        Assert.False(GpuMemorySensor.TryParseLuid("_Total", out _));
    }
}

public class TemperaturePlanTests
{
    private static readonly TimeSpan Two = TimeSpan.FromSeconds(2);

    [Fact]
    public void Temperature_is_read_only_for_active_medium_or_large_widgets_that_want_it()
    {
        var plan = SamplingPlan.From(
        [
            new(true, CardSize.Medium, Two, TemperatureLuid: 1),
            new(true, CardSize.Small, Two, TemperatureLuid: 2),   // small cards show no temperature
            new(false, CardSize.Large, Two, TemperatureLuid: 3),  // inactive
            new(true, CardSize.Large, Two, TemperatureLuid: null),// setting off
        ]);
        Assert.Equal(new LuidSet([1]), plan.TemperatureLuids);
        Assert.True(plan.TemperatureLuids.Contains(1));
        Assert.False(plan.TemperatureLuids.Contains(2));
    }

    [Fact]
    public void Plans_with_the_same_adapters_are_equal()
    {
        var a = new SamplingPlan(true, CardSize.Large, Two, new LuidSet([3, 1, 1]));
        var b = new SamplingPlan(true, CardSize.Large, Two, new LuidSet([1, 3]));
        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { TemperatureLuids = default });
    }

    [Fact]
    public void Temperature_sensor_runs_only_when_some_adapter_is_requested()
    {
        var sensor = new GpuTemperatureSensor(new GpuAdapterCatalog(() => []));
        ISensor s = sensor;
        Assert.False(s.IsNeededFor(new SamplingPlan(true, CardSize.Large, Two)));
        Assert.True(s.IsNeededFor(new SamplingPlan(true, CardSize.Large, Two, new LuidSet([1]))));
        Assert.False(s.IsNeededFor(new SamplingPlan(true, CardSize.Small, Two, new LuidSet([1]))));
    }
}

public class AdapterCatalogTests
{
    [Fact]
    public void Ignores_software_adapters_and_detects_hybrid_graphics()
    {
        var catalog = new GpuAdapterCatalog(() =>
        [
            new GpuAdapterInfo(1, "Intel(R) Graphics", 0x8086, 128UL << 20, false),
            new GpuAdapterInfo(2, "NVIDIA GeForce RTX 4060 Laptop GPU", 0x10DE, 8UL << 30, false),
            new GpuAdapterInfo(3, "Microsoft Basic Render Driver", 0x1414, 0, true),
        ]);
        Assert.Equal(2, catalog.Adapters.Count);
        Assert.True(catalog.IsHybrid);
        Assert.Equal(2, catalog.Default!.Luid);
        Assert.Equal(1, catalog.FindByName("Intel(R) Graphics")!.Luid);
        Assert.Null(catalog.FindByName(null));
    }

    [Fact]
    public void Enumeration_failure_leaves_an_empty_list()
    {
        var catalog = new GpuAdapterCatalog(() => throw new InvalidOperationException("no DXGI"));
        Assert.Empty(catalog.Adapters);
        Assert.Null(catalog.Default);
        Assert.False(catalog.IsHybrid);
    }
}
