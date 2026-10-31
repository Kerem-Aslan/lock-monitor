using LockMonitor.Rendering;
using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>How full the Windows system drive is. Changes slowly, so it is read every 30 seconds.</summary>
public sealed class DiskSensor : ISensor
{
    private DriveInfo? _drive;

    public string Name => "System drive";
    public CardSize RequiredSize => CardSize.Medium;
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(30);

    public void Open()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        _drive = new DriveInfo(root);
    }

    public void Sample(SystemSnapshot snapshot, in SamplingPlan plan)
    {
        var drive = _drive!;
        long total = drive.TotalSize;
        long free = drive.TotalFreeSpace;
        snapshot.SystemDriveName = drive.Name.TrimEnd('\\');
        snapshot.SystemDriveTotalBytes = (ulong)total;
        snapshot.SystemDriveUsedBytes = (ulong)(total - free);
    }

    public void Clear(SystemSnapshot snapshot)
    {
        snapshot.SystemDriveName = null;
        snapshot.SystemDriveTotalBytes = null;
        snapshot.SystemDriveUsedBytes = null;
    }

    public void Close() => _drive = null;
}
