using System.Runtime.InteropServices;

namespace LockMonitor.Native;

/// <summary>
/// Kernel-mode thunks exported by gdi32.dll for user mode (documented in the WDK, d3dkmthk.h).
/// Used read-only for the adapter performance data that Task Manager shows (temperature).
/// Works for standard users; needs a WDDM 2.4+ driver that fills the fields.
/// </summary>
internal static partial class D3dkmt
{
    /// <summary>KMTQUERYADAPTERINFOTYPE value for D3DKMT_ADAPTER_PERFDATA (verified on Windows 11 26300).</summary>
    public const int KMTQAITYPE_ADAPTERPERFDATA = 62;

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_OPENADAPTERFROMLUID
    {
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint hAdapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_CLOSEADAPTER
    {
        public uint hAdapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public int Type;
        public nint pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    /// <summary>
    /// D3DKMT_ADAPTER_PERFDATA. The 64-bit fields are D3DKMT_ALIGN64, which sequential layout
    /// reproduces with 4 bytes of padding after PhysicalAdapterIndex (64 bytes in total).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PCIEBandwidth;
        public uint FanRPM;
        public uint Power;        // tenths of a percent
        public uint Temperature;  // tenths of a degree Celsius; 0 = not reported
        public byte PowerStateOverride;
    }

    [LibraryImport("gdi32.dll")]
    public static partial int D3DKMTOpenAdapterFromLuid(ref D3DKMT_OPENADAPTERFROMLUID args);

    [LibraryImport("gdi32.dll")]
    public static partial int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO args);

    [LibraryImport("gdi32.dll")]
    public static partial int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER args);

    public static uint OpenAdapter(long luid)
    {
        var open = new D3DKMT_OPENADAPTERFROMLUID { AdapterLuidLow = (uint)luid, AdapterLuidHigh = (int)(luid >> 32) };
        int status = D3DKMTOpenAdapterFromLuid(ref open);
        if (status != 0)
        {
            throw new InvalidOperationException($"D3DKMTOpenAdapterFromLuid failed with NTSTATUS 0x{status:X8}");
        }
        return open.hAdapter;
    }

    public static void CloseAdapter(uint handle)
    {
        var close = new D3DKMT_CLOSEADAPTER { hAdapter = handle };
        D3DKMTCloseAdapter(ref close);
    }

    /// <summary>GPU temperature in °C, or null when the driver does not report it.</summary>
    public static unsafe double? QueryTemperature(uint adapterHandle)
    {
        D3DKMT_ADAPTER_PERFDATA data = default;
        var query = new D3DKMT_QUERYADAPTERINFO
        {
            hAdapter = adapterHandle,
            Type = KMTQAITYPE_ADAPTERPERFDATA,
            pPrivateDriverData = (nint)(&data),
            PrivateDriverDataSize = (uint)sizeof(D3DKMT_ADAPTER_PERFDATA),
        };
        int status = D3DKMTQueryAdapterInfo(ref query);
        if (status != 0)
        {
            throw new InvalidOperationException($"D3DKMTQueryAdapterInfo(ADAPTERPERFDATA) failed with NTSTATUS 0x{status:X8}");
        }
        return data.Temperature == 0 ? null : data.Temperature / 10.0;
    }
}
