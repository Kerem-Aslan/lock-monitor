using System.Globalization;
using LockMonitor.Native;
using LockMonitor.Rendering;
using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>
/// Video memory in use per adapter from the "GPU Adapter Memory" counters (system-wide, what Task
/// Manager shows). Total = DXGI DedicatedVideoMemory. Integrated GPUs have little or no dedicated
/// memory, so for them shared system memory is reported instead.
/// </summary>
public sealed class GpuMemorySensor(GpuAdapterCatalog adapters) : ISensor
{
    /// <summary>Below this much dedicated memory an adapter is treated as integrated.</summary>
    internal const ulong IntegratedThreshold = 512UL * 1024 * 1024;

    private readonly Dictionary<long, double> _dedicated = [];
    private readonly Dictionary<long, double> _shared = [];
    private PdhQuery.InstanceVisitor? _visitDedicated;
    private PdhQuery.InstanceVisitor? _visitShared;
    private PdhQuery? _query;
    private nint _dedicatedCounter;
    private nint _sharedCounter;

    public string Name => "GPU memory";
    public CardSize RequiredSize => CardSize.Medium;
    public TimeSpan RefreshInterval => TimeSpan.Zero;

    public void Open()
    {
        _visitDedicated ??= (name, value) => Accumulate(_dedicated, name, value);
        _visitShared ??= (name, value) => Accumulate(_shared, name, value);
        _query = new PdhQuery();
        _dedicatedCounter = _query.AddCounter(@"\GPU Adapter Memory(*)\Dedicated Usage");
        _sharedCounter = _query.AddCounter(@"\GPU Adapter Memory(*)\Shared Usage");
    }

    public void Sample(SystemSnapshot snapshot, in SamplingPlan plan)
    {
        _query!.Collect();
        _dedicated.Clear();
        _shared.Clear();
        _query.VisitArray(_dedicatedCounter, _visitDedicated!);
        _query.VisitArray(_sharedCounter, _visitShared!);

        foreach (var adapter in adapters.Adapters)
        {
            var gpu = snapshot.GetOrAddGpu(adapter.Luid, adapter.Name);
            bool integrated = adapter.DedicatedVideoMemory < IntegratedThreshold;
            gpu.MemoryIsShared = integrated;
            if (integrated)
            {
                gpu.MemoryUsedBytes = _shared.TryGetValue(adapter.Luid, out var s) ? (ulong)s : null;
                gpu.MemoryTotalBytes = adapter.SharedSystemMemory > 0 ? adapter.SharedSystemMemory : null;
            }
            else
            {
                gpu.MemoryUsedBytes = _dedicated.TryGetValue(adapter.Luid, out var d) ? (ulong)d : null;
                gpu.MemoryTotalBytes = adapter.DedicatedVideoMemory;
            }
        }
    }

    /// <summary>Instances look like "luid_0x00000000_0x00011D3C_phys_0"; several physical parts add up.</summary>
    private static void Accumulate(Dictionary<long, double> totals, ReadOnlySpan<char> name, double value)
    {
        if (TryParseLuid(name, out long luid) && double.IsFinite(value))
        {
            totals[luid] = totals.GetValueOrDefault(luid) + value;
        }
    }

    internal static bool TryParseLuid(ReadOnlySpan<char> name, out long luid)
    {
        luid = 0;
        int at = name.IndexOf("luid_0x", StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return false;
        }
        var rest = name[(at + "luid_0x".Length)..];
        int split = rest.IndexOf("_0x", StringComparison.OrdinalIgnoreCase);
        if (split <= 0)
        {
            return false;
        }
        var high = rest[..split];
        rest = rest[(split + 3)..];
        int end = rest.IndexOf('_');
        var low = end < 0 ? rest : rest[..end];
        if (!uint.TryParse(high, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint h) ||
            !uint.TryParse(low, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint l))
        {
            return false;
        }
        luid = ((long)h << 32) | l;
        return true;
    }

    public void Clear(SystemSnapshot snapshot)
    {
        foreach (var gpu in snapshot.Gpus)
        {
            gpu.MemoryUsedBytes = null;
            gpu.MemoryTotalBytes = null;
        }
    }

    public void Close()
    {
        _query?.Dispose();
        _query = null;
    }
}
