using LockMonitor.Sensors;
using Xunit;

namespace LockMonitor.Tests;

public class GpuEngineAggregatorTests
{
    private const long Nvidia = 0x00011D3C;
    private const long Intel = 0x1_0000F00D;

    [Theory]
    [InlineData("pid_1000_luid_0x00000000_0x00011D3C_phys_0_eng_0_engtype_3D", 0x00011D3CL, "3D")]
    [InlineData("pid_4_luid_0x00000001_0x0000F00D_phys_0_eng_12_engtype_VideoDecode", 0x1_0000F00DL, "VideoDecode")]
    [InlineData("pid_77_luid_0x00000000_0x00011d3c_phys_0_eng_5_engtype_compute_0", 0x00011D3CL, "compute_0")]
    public void Parses_instance_names(string name, long expectedLuid, string expectedType)
    {
        Assert.True(GpuEngineAggregator.TryParseInstance(name, out long luid, out var type));
        Assert.Equal(expectedLuid, luid);
        Assert.Equal(expectedType, type.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("_Total")]
    [InlineData("pid_1_luid_0xZZ_0x01_phys_0_eng_0_engtype_3D")]
    [InlineData("pid_1_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_")]
    [InlineData("pid_1_phys_0_eng_0_engtype_3D")]
    public void Rejects_malformed_instance_names(string name)
    {
        Assert.False(GpuEngineAggregator.TryParseInstance(name, out _, out _));
    }

    [Fact]
    public void Sums_processes_per_engine_type_and_reports_the_busiest_engine()
    {
        var agg = new GpuEngineAggregator();
        agg.Add("pid_1_luid_0x00000000_0x00011D3C_phys_0_eng_0_engtype_3D", 20);
        agg.Add("pid_2_luid_0x00000000_0x00011D3C_phys_0_eng_0_engtype_3D", 15);
        agg.Add("pid_2_luid_0x00000000_0x00011D3C_phys_0_eng_3_engtype_Copy", 30);
        agg.Add("pid_3_luid_0x00000001_0x0000F00D_phys_0_eng_0_engtype_3D", 90);

        Assert.Equal(35, agg.UsageFor(Nvidia)); // 3D: 20 + 15 beats Copy: 30
        Assert.Equal(90, agg.UsageFor(Intel));
        Assert.Equal(90, agg.MaxUsage());
    }

    [Fact]
    public void Engine_types_match_case_insensitively()
    {
        var agg = new GpuEngineAggregator();
        agg.Add("pid_1_luid_0x00000000_0x00011D3C_phys_0_eng_0_engtype_3D", 10);
        agg.Add("pid_2_luid_0x00000000_0x00011D3C_phys_0_eng_0_engtype_3d", 10);
        Assert.Equal(20, agg.UsageFor(Nvidia));
    }

    [Fact]
    public void Caps_at_100_and_ignores_non_finite_values()
    {
        var agg = new GpuEngineAggregator();
        agg.Add("pid_1_luid_0x00000000_0x00011D3C_phys_0_eng_0_engtype_3D", 70);
        agg.Add("pid_2_luid_0x00000000_0x00011D3C_phys_0_eng_0_engtype_3D", 70);
        agg.Add("pid_3_luid_0x00000000_0x00011D3C_phys_0_eng_0_engtype_3D", double.NaN);
        Assert.Equal(100, agg.UsageFor(Nvidia));
    }

    [Fact]
    public void Unknown_adapter_has_no_reading_and_reset_clears_everything()
    {
        var agg = new GpuEngineAggregator();
        agg.Add("pid_1_luid_0x00000000_0x00011D3C_phys_0_eng_0_engtype_3D", 10);
        Assert.Null(agg.UsageFor(Intel));

        agg.Reset();
        Assert.Null(agg.UsageFor(Nvidia));
        Assert.Null(agg.MaxUsage());
    }

    [Fact]
    public void Default_adapter_is_the_hardware_adapter_with_most_video_memory()
    {
        var adapters = new[]
        {
            new Native.GpuAdapterInfo(1, "Intel(R) Graphics", 0x8086, 128UL << 20, false),
            new Native.GpuAdapterInfo(2, "NVIDIA GeForce RTX 4060 Laptop GPU", 0x10DE, 8UL << 30, false),
            new Native.GpuAdapterInfo(3, "Microsoft Basic Render Driver", 0x1414, 0, true),
        };
        Assert.Equal(2, GpuAdapterSelector.SelectDefault(adapters)!.Luid);
        Assert.Equal(2, GpuAdapterSelector.CountHardware(adapters));
        Assert.Null(GpuAdapterSelector.SelectDefault([adapters[2]]));
    }
}
