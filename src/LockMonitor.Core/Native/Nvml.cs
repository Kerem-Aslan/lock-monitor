using System.Runtime.InteropServices;

namespace LockMonitor.Native;

/// <summary>
/// NVIDIA Management Library, loaded at runtime from the copy the NVIDIA driver installs
/// (System32 for DCH drivers, otherwise the NVSMI folder). Nothing is redistributed.
/// Used only as a fallback when D3DKMT does not report a temperature for an NVIDIA adapter.
/// </summary>
internal sealed unsafe class Nvml : IDisposable
{
    private const int NVML_SUCCESS = 0;
    private const int NVML_TEMPERATURE_GPU = 0;

    private nint _library;
    private readonly delegate* unmanaged[Cdecl]<int> _shutdown;
    private readonly delegate* unmanaged[Cdecl]<uint*, int> _getCount;
    private readonly delegate* unmanaged[Cdecl]<uint, nint*, int> _getHandleByIndex;
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, uint, int> _getName;
    private readonly delegate* unmanaged[Cdecl]<nint, NvmlTemperatureV1*, int> _getTemperatureV;
    private readonly delegate* unmanaged[Cdecl]<nint, int, uint*, int> _getTemperature;

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlTemperatureV1
    {
        public uint Version;
        public int SensorType;
        public int Temperature;
    }

    private Nvml(nint library)
    {
        _library = library;
        _shutdown = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(library, "nvmlShutdown");
        _getCount = (delegate* unmanaged[Cdecl]<uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetCount_v2");
        _getHandleByIndex = (delegate* unmanaged[Cdecl]<uint, nint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetHandleByIndex_v2");
        _getName = (delegate* unmanaged[Cdecl]<nint, byte*, uint, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetName");
        // nvmlDeviceGetTemperatureV replaces the deprecated nvmlDeviceGetTemperature (CUDA 13); use whichever exists.
        _getTemperatureV = NativeLibrary.TryGetExport(library, "nvmlDeviceGetTemperatureV", out var v)
            ? (delegate* unmanaged[Cdecl]<nint, NvmlTemperatureV1*, int>)v : null;
        _getTemperature = NativeLibrary.TryGetExport(library, "nvmlDeviceGetTemperature", out var legacy)
            ? (delegate* unmanaged[Cdecl]<nint, int, uint*, int>)legacy : null;
    }

    /// <summary>Loads and initialises NVML, or returns null when no NVIDIA driver provides it.</summary>
    public static Nvml? TryLoad()
    {
        string[] candidates =
        [
            Path.Combine(Environment.SystemDirectory, "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
        ];
        foreach (var path in candidates)
        {
            if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var library))
            {
                continue;
            }
            try
            {
                var init = (delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(library, "nvmlInit_v2");
                if (init() == NVML_SUCCESS)
                {
                    return new Nvml(library);
                }
            }
            catch (EntryPointNotFoundException)
            {
            }
            NativeLibrary.Free(library);
        }
        return null;
    }

    /// <summary>Temperature of the NVIDIA device whose name matches the DXGI adapter name.</summary>
    public double? TemperatureByName(string adapterName)
    {
        uint count;
        if (_getCount(&count) != NVML_SUCCESS)
        {
            return null;
        }
        byte* name = stackalloc byte[96];
        for (uint i = 0; i < count; i++)
        {
            nint device;
            if (_getHandleByIndex(i, &device) != NVML_SUCCESS || _getName(device, name, 96) != NVML_SUCCESS)
            {
                continue;
            }
            if (!string.Equals(Marshal.PtrToStringUTF8((nint)name), adapterName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (_getTemperatureV is not null)
            {
                var t = new NvmlTemperatureV1 { Version = (uint)sizeof(NvmlTemperatureV1) | (1u << 24), SensorType = NVML_TEMPERATURE_GPU };
                if (_getTemperatureV(device, &t) == NVML_SUCCESS)
                {
                    return t.Temperature;
                }
            }
            if (_getTemperature is not null)
            {
                uint celsius;
                if (_getTemperature(device, NVML_TEMPERATURE_GPU, &celsius) == NVML_SUCCESS)
                {
                    return celsius;
                }
            }
            return null;
        }
        return null;
    }

    public void Dispose()
    {
        if (_library != 0)
        {
            _shutdown();
            NativeLibrary.Free(_library);
            _library = 0;
        }
    }
}
