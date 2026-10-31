using LockMonitor.Native;
using LockMonitor.Rendering;
using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>
/// GPU usage from the "GPU Engine" performance counters (Task Manager's source, WDDM 2.0+).
/// One wildcard PDH counter is used; PDH re-expands the wildcard on every collection, so engines
/// of processes started later are included (unlike enumerating instance names once).
/// </summary>
public sealed class GpuUsageSensor : ISensor
{
    private const string EnginePath = @"\GPU Engine(*)\Utilization Percentage";

    private readonly GpuEngineAggregator _aggregator = new();
    private readonly PdhQuery.InstanceVisitor _visitor;
    private PdhQuery? _query;
    private nint _counter;
    private GpuAdapterInfo? _adapter;

    public GpuUsageSensor()
    {
        _visitor = _aggregator.Add; // cached so sampling does not allocate a delegate per tick
    }

    public string Name => "GPU usage";
    public CardSize RequiredSize => CardSize.Small;
    public TimeSpan RefreshInterval => TimeSpan.Zero;

    /// <summary>Adapter the reading belongs to; null when DXGI could not enumerate adapters.</summary>
    public GpuAdapterInfo? Adapter => _adapter;

    public void Open()
    {
        try
        {
            _adapter = GpuAdapterSelector.SelectDefault(Dxgi.EnumerateAdapters());
        }
        catch (Exception ex)
        {
            // Usage still works without DXGI: the busiest adapter is reported instead.
            Log.Error("DXGI adapter enumeration failed", ex);
            _adapter = null;
        }

        _query = new PdhQuery();
        _counter = _query.AddCounter(EnginePath);
        _query.Collect(); // rate counter baseline
    }

    public void Sample(SystemSnapshot snapshot)
    {
        _query!.Collect();
        _aggregator.Reset();
        _query.VisitArray(_counter, _visitor);

        snapshot.GpuName = _adapter?.Name;
        snapshot.GpuPercent = _adapter is { } adapter
            ? _aggregator.UsageFor(adapter.Luid) ?? 0 // no engine instances = idle (or powered down)
            : _aggregator.MaxUsage();
    }

    public void Clear(SystemSnapshot snapshot)
    {
        snapshot.GpuPercent = null;
        snapshot.GpuName = null;
    }

    public void Close()
    {
        _query?.Dispose();
        _query = null;
        _counter = 0;
    }
}

public static class GpuAdapterSelector
{
    /// <summary>
    /// Default adapter: the hardware adapter with the most dedicated video memory, which is the
    /// discrete GPU on hybrid laptops. Software adapters (Microsoft Basic Render Driver) are ignored.
    /// </summary>
    public static GpuAdapterInfo? SelectDefault(IEnumerable<GpuAdapterInfo> adapters) =>
        adapters.Where(a => a.IsHardware)
                .OrderByDescending(a => a.DedicatedVideoMemory)
                .FirstOrDefault();

    public static int CountHardware(IEnumerable<GpuAdapterInfo> adapters) => adapters.Count(a => a.IsHardware);
}
