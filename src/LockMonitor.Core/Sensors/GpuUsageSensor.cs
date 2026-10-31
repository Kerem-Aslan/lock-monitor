using LockMonitor.Native;
using LockMonitor.Rendering;
using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>
/// GPU usage per hardware adapter from the "GPU Engine" performance counters (Task Manager's
/// source, WDDM 2.0+). One wildcard PDH counter is used; PDH re-expands the wildcard on every
/// collection, so engines of processes started later are included.
/// </summary>
public sealed class GpuUsageSensor(GpuAdapterCatalog adapters) : ISensor
{
    private const string EnginePath = @"\GPU Engine(*)\Utilization Percentage";

    private readonly GpuEngineAggregator _aggregator = new();
    private PdhQuery.InstanceVisitor? _visitor;
    private PdhQuery? _query;
    private nint _counter;

    public string Name => "GPU usage";
    public CardSize RequiredSize => CardSize.Small;
    public TimeSpan RefreshInterval => TimeSpan.Zero;

    public void Open()
    {
        adapters.Refresh(); // sampling (re)starts here: pick up added/removed adapters
        _visitor ??= _aggregator.Add; // cached so sampling does not allocate a delegate per tick
        _query = new PdhQuery();
        _counter = _query.AddCounter(EnginePath);
        _query.Collect(); // rate counter baseline
    }

    public void Sample(SystemSnapshot snapshot, in SamplingPlan plan)
    {
        _query!.Collect();
        _aggregator.Reset();
        _query.VisitArray(_counter, _visitor!);

        snapshot.DefaultGpuLuid = adapters.Default?.Luid;
        foreach (var adapter in adapters.Adapters)
        {
            // No engine instances = idle (or powered down) adapter.
            snapshot.GetOrAddGpu(adapter.Luid, adapter.Name).UsagePercent = _aggregator.UsageFor(adapter.Luid) ?? 0;
        }
        snapshot.Gpus.RemoveAll(g => adapters.Find(g.Luid) is null);
    }

    public void Clear(SystemSnapshot snapshot)
    {
        foreach (var gpu in snapshot.Gpus)
        {
            gpu.UsagePercent = null;
        }
    }

    public void Close()
    {
        _query?.Dispose();
        _query = null;
        _counter = 0;
    }
}
