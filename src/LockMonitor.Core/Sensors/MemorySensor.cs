using System.ComponentModel;
using System.Runtime.InteropServices;
using LockMonitor.Native;
using LockMonitor.Rendering;
using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>Physical memory in use (total minus available), from GlobalMemoryStatusEx.</summary>
public sealed class MemorySensor : ISensor
{
    public string Name => "Memory";
    public CardSize RequiredSize => CardSize.Small;
    public TimeSpan RefreshInterval => TimeSpan.Zero;

    public void Open()
    {
    }

    public void Sample(SystemSnapshot snapshot)
    {
        var status = new Kernel32.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Kernel32.MEMORYSTATUSEX>() };
        if (!Kernel32.GlobalMemoryStatusEx(ref status))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        ulong used = status.ullTotalPhys - status.ullAvailPhys;
        snapshot.MemoryTotalBytes = status.ullTotalPhys;
        snapshot.MemoryUsedBytes = used;
        snapshot.MemoryPercent = status.ullTotalPhys == 0 ? null : 100.0 * used / status.ullTotalPhys;
    }

    public void Clear(SystemSnapshot snapshot)
    {
        snapshot.MemoryPercent = null;
        snapshot.MemoryUsedBytes = null;
        snapshot.MemoryTotalBytes = null;
    }

    public void Close()
    {
    }
}
