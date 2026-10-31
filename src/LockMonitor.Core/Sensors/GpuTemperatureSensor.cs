using LockMonitor.Native;
using LockMonitor.Rendering;
using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>
/// GPU temperature through D3DKMTQueryAdapterInfo(ADAPTERPERFDATA), the same data Task Manager
/// shows (WDDM 2.4+, filled by most discrete-GPU drivers). NVIDIA adapters whose driver reports 0
/// fall back to NVML from the installed driver.
/// <para>Runs only for adapters that an active medium/large widget shows a temperature for (see
/// <see cref="SamplingPlan.TemperatureLuids"/>). The user setting is off by default on hybrid
/// laptops because querying the discrete GPU can keep it awake.</para>
/// </summary>
public sealed class GpuTemperatureSensor(GpuAdapterCatalog adapters) : ISensor
{
    private const uint NvidiaVendorId = 0x10DE;

    private readonly Dictionary<long, uint> _handles = [];
    private Nvml? _nvml;
    private bool _nvmlTried;

    public string Name => "GPU temperature";
    public CardSize RequiredSize => CardSize.Medium;
    public TimeSpan RefreshInterval => TimeSpan.Zero;

    public bool IsNeededFor(in SamplingPlan plan) => plan.Size >= RequiredSize && !plan.TemperatureLuids.IsEmpty;

    public void Open()
    {
    }

    public void Sample(SystemSnapshot snapshot, in SamplingPlan plan)
    {
        // Release adapters nobody asks for any more, so they are not touched while unwanted.
        foreach (var luid in _handles.Keys.ToArray())
        {
            if (!plan.TemperatureLuids.Contains(luid))
            {
                D3dkmt.CloseAdapter(_handles[luid]);
                _handles.Remove(luid);
            }
        }

        foreach (var gpu in snapshot.Gpus)
        {
            if (!plan.TemperatureLuids.Contains(gpu.Luid))
            {
                gpu.TemperatureCelsius = null;
                continue;
            }
            gpu.TemperatureCelsius = Read(gpu.Luid);
        }
    }

    private double? Read(long luid)
    {
        double? celsius = null;
        try
        {
            if (!_handles.TryGetValue(luid, out uint handle))
            {
                handle = D3dkmt.OpenAdapter(luid);
                _handles[luid] = handle;
            }
            celsius = D3dkmt.QueryTemperature(handle);
        }
        catch (InvalidOperationException)
        {
            // Driver without ADAPTERPERFDATA support; try NVML below.
        }

        if (celsius is null && adapters.Find(luid) is { VendorId: NvidiaVendorId } nvidia)
        {
            if (!_nvmlTried)
            {
                _nvmlTried = true;
                _nvml = Nvml.TryLoad();
            }
            celsius = _nvml?.TemperatureByName(nvidia.Name);
        }
        return celsius;
    }

    public void Clear(SystemSnapshot snapshot)
    {
        foreach (var gpu in snapshot.Gpus)
        {
            gpu.TemperatureCelsius = null;
        }
    }

    public void Close()
    {
        foreach (var handle in _handles.Values)
        {
            D3dkmt.CloseAdapter(handle);
        }
        _handles.Clear();
        _nvml?.Dispose();
        _nvml = null;
        _nvmlTried = false;
    }
}
