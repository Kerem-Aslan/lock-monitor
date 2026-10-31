namespace LockMonitor.Sampling;

/// <summary>
/// One set of readings. A null value means "not available" (sensor unsupported, failing or not
/// running for the current card size); the card shows N/A or hides the row. A fresh copy is
/// published on every tick, so readers never see a half-written snapshot.
/// </summary>
public sealed class SystemSnapshot
{
    public DateTimeOffset Timestamp { get; set; }

    public double? CpuPercent { get; set; }

    public double? MemoryPercent { get; set; }
    public ulong? MemoryUsedBytes { get; set; }
    public ulong? MemoryTotalBytes { get; set; }

    /// <summary>One entry per hardware graphics adapter.</summary>
    public List<GpuReading> Gpus { get; private set; } = [];

    /// <summary>Adapter shown when a widget does not pick one (most dedicated video memory).</summary>
    public long? DefaultGpuLuid { get; set; }

    public double? NetworkDownBytesPerSecond { get; set; }
    public double? NetworkUpBytesPerSecond { get; set; }

    /// <summary>Fixed drives (letter order); null until the disk sensor has run.</summary>
    public IReadOnlyList<DriveReading>? Drives { get; set; }

    /// <summary>
    /// ACPI thermal zone temperature (not the CPU die). null when the firmware exposes no zone or
    /// the value never changes (a fixed placeholder); the card then hides the row.
    /// </summary>
    public double? SystemTemperatureCelsius { get; set; }

    /// <summary>null when the PC has no battery.</summary>
    public BatteryReading? Battery { get; set; }

    public TimeSpan? Uptime { get; set; }

    /// <summary>Apps using the most CPU over the last interval, busiest first.</summary>
    public IReadOnlyList<ProcessUsage>? TopProcesses { get; set; }

    public DriveReading? FindDrive(string name) =>
        Drives?.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    public GpuReading? FindGpu(long luid) => Gpus.Find(g => g.Luid == luid);

    public GpuReading GetOrAddGpu(long luid, string name)
    {
        var gpu = FindGpu(luid);
        if (gpu is null)
        {
            gpu = new GpuReading(luid, name);
            Gpus.Add(gpu);
        }
        return gpu;
    }

    /// <summary>
    /// Copies the previous readings so sensors refreshed less often keep their last value.
    /// GPU readings are copied too, so the published snapshot is never mutated afterwards.
    /// </summary>
    public SystemSnapshot Clone()
    {
        var copy = (SystemSnapshot)MemberwiseClone();
        copy.Gpus = Gpus.ConvertAll(g => g.Clone());
        return copy;
    }
}

/// <summary>Readings for one graphics adapter.</summary>
public sealed class GpuReading(long luid, string name)
{
    public long Luid { get; } = luid;
    public string Name { get; } = name;
    public double? UsagePercent { get; set; }
    public ulong? MemoryUsedBytes { get; set; }
    public ulong? MemoryTotalBytes { get; set; }

    /// <summary>True when the memory figures are shared system memory (integrated GPUs).</summary>
    public bool MemoryIsShared { get; set; }

    public double? TemperatureCelsius { get; set; }

    public double? MemoryPercent =>
        MemoryTotalBytes is > 0 && MemoryUsedBytes is { } used ? 100.0 * used / MemoryTotalBytes.Value : null;

    public GpuReading Clone() => (GpuReading)MemberwiseClone();
}

/// <summary>Space on one fixed drive. Immutable, so snapshots can share it.</summary>
/// <param name="Name">"C:"</param>
/// <param name="Label">Volume label, may be empty.</param>
public sealed record DriveReading(string Name, string Label, bool IsSystem, ulong UsedBytes, ulong TotalBytes)
{
    public double? Percent => TotalBytes > 0 ? 100.0 * UsedBytes / TotalBytes : null;
}

public enum ChargeState
{
    Discharging,
    Charging,
    /// <summary>On AC power but not charging (full, or charging paused by the firmware).</summary>
    PluggedIn,
}

/// <summary>Battery state. Immutable.</summary>
/// <param name="TimeRemaining">Estimated time left on battery, when Windows has an estimate.</param>
public sealed record BatteryReading(int Percent, ChargeState State, TimeSpan? TimeRemaining);

/// <summary>CPU use of all processes with one image name ("chrome" = every chrome.exe).</summary>
public sealed record ProcessUsage(string Name, double CpuPercent);
