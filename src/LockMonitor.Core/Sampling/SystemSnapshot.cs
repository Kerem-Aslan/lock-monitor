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

    public string? SystemDriveName { get; set; }
    public ulong? SystemDriveUsedBytes { get; set; }
    public ulong? SystemDriveTotalBytes { get; set; }
    public double? SystemDrivePercent =>
        SystemDriveTotalBytes is > 0 && SystemDriveUsedBytes is { } used ? 100.0 * used / SystemDriveTotalBytes.Value : null;

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
