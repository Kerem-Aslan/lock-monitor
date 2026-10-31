using System.Runtime.InteropServices;

namespace LockMonitor.Native;

/// <summary>
/// Minimal DXGI adapter enumeration through raw vtable calls (no COM interop marshalling, AOT safe).
/// Enumerating adapters and reading their descriptions does not create a device, so it does not
/// power up a sleeping discrete GPU.
/// </summary>
internal static unsafe partial class Dxgi
{
    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    public const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;

    // Vtable slots: IUnknown 0-2, IDXGIObject 3-6, IDXGIFactory 7-11, IDXGIFactory1.EnumAdapters1 = 12
    private const int Slot_Release = 2;
    private const int Slot_Factory1_EnumAdapters1 = 12;
    // IDXGIAdapter 7-9, IDXGIAdapter1.GetDesc1 = 10
    private const int Slot_Adapter1_GetDesc1 = 10;

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(in Guid riid, out nint factory);

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_ADAPTER_DESC1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    public static List<GpuAdapterInfo> EnumerateAdapters()
    {
        var result = new List<GpuAdapterInfo>();
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(IID_IDXGIFactory1, out nint factory));
        try
        {
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)(*(void***)factory)[Slot_Factory1_EnumAdapters1];
            for (uint i = 0; ; i++)
            {
                nint adapter;
                int hr = enumAdapters1(factory, i, &adapter);
                if (hr == DXGI_ERROR_NOT_FOUND)
                {
                    break;
                }
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    var getDesc1 = (delegate* unmanaged[Stdcall]<nint, DXGI_ADAPTER_DESC1*, int>)(*(void***)adapter)[Slot_Adapter1_GetDesc1];
                    DXGI_ADAPTER_DESC1 desc;
                    Marshal.ThrowExceptionForHR(getDesc1(adapter, &desc));
                    result.Add(new GpuAdapterInfo(
                        Luid: ((long)desc.AdapterLuidHigh << 32) | desc.AdapterLuidLow,
                        Name: new string(desc.Description).TrimEnd('\0'),
                        VendorId: desc.VendorId,
                        DedicatedVideoMemory: (ulong)desc.DedicatedVideoMemory,
                        SharedSystemMemory: (ulong)desc.SharedSystemMemory,
                        IsSoftware: (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0));
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }
        return result;
    }

    private static void Release(nint unknown)
    {
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)unknown)[Slot_Release];
        release(unknown);
    }
}

/// <summary>One graphics adapter as reported by DXGI.</summary>
public sealed record GpuAdapterInfo(long Luid, string Name, uint VendorId, ulong DedicatedVideoMemory, bool IsSoftware, ulong SharedSystemMemory = 0)
{
    public bool IsHardware => !IsSoftware;
}
