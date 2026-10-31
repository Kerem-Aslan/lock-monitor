using LockMonitor.Native;

namespace LockMonitor.Sensors;

/// <summary>
/// The machine's hardware graphics adapters, shared by the GPU sensors and the settings card.
/// Enumerated through DXGI, which does not create a device and so does not wake a sleeping
/// discrete GPU. Refreshed whenever sampling starts (adapters can come and go, e.g. eGPUs).
/// </summary>
public sealed class GpuAdapterCatalog
{
    private readonly Func<IReadOnlyList<GpuAdapterInfo>> _enumerate;
    private volatile IReadOnlyList<GpuAdapterInfo> _adapters = [];
    private volatile bool _loaded;

    public GpuAdapterCatalog()
        : this(Dxgi.EnumerateAdapters)
    {
    }

    /// <summary>Test seam: supply the adapter list.</summary>
    public GpuAdapterCatalog(Func<IReadOnlyList<GpuAdapterInfo>> enumerate)
    {
        _enumerate = enumerate;
    }

    /// <summary>Hardware adapters (software renderers excluded), loaded on first use.</summary>
    public IReadOnlyList<GpuAdapterInfo> Adapters
    {
        get
        {
            if (!_loaded)
            {
                Refresh();
            }
            return _adapters;
        }
    }

    /// <summary>The adapter with the most dedicated video memory: the discrete GPU on hybrid laptops.</summary>
    public GpuAdapterInfo? Default => GpuAdapterSelector.SelectDefault(Adapters);

    /// <summary>
    /// Two or more hardware GPUs (hybrid graphics). Reading the discrete GPU's temperature can keep
    /// it awake there, so the temperature setting defaults to off on such machines.
    /// </summary>
    public bool IsHybrid => Adapters.Count >= 2;

    public GpuAdapterInfo? Find(long luid) => Adapters.FirstOrDefault(a => a.Luid == luid);

    public GpuAdapterInfo? FindByName(string? name) =>
        string.IsNullOrEmpty(name) ? null : Adapters.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));

    public void Refresh()
    {
        try
        {
            _adapters = [.. _enumerate().Where(a => a.IsHardware)];
        }
        catch (Exception ex)
        {
            Log.Error("GPU adapter enumeration failed", ex);
            _adapters = [];
        }
        _loaded = true;
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
