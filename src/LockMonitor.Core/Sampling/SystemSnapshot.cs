namespace LockMonitor.Sampling;

/// <summary>
/// One set of readings. A null value means "not available" (sensor unsupported or failing);
/// the card shows N/A for it. A fresh copy is published on every tick, so readers never see a
/// half-written snapshot.
/// </summary>
public sealed class SystemSnapshot
{
    public DateTimeOffset Timestamp { get; set; }

    public double? CpuPercent { get; set; }

    public double? MemoryPercent { get; set; }
    public ulong? MemoryUsedBytes { get; set; }
    public ulong? MemoryTotalBytes { get; set; }

    public double? GpuPercent { get; set; }
    public string? GpuName { get; set; }

    /// <summary>Copies the previous readings so sensors refreshed less often keep their last value.</summary>
    public SystemSnapshot Clone() => (SystemSnapshot)MemberwiseClone();
}
