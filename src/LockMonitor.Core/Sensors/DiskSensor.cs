using LockMonitor.Rendering;
using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>
/// How full each fixed drive is. Space changes slowly, so drives are read every 30 seconds.
/// Which drives a widget shows is a per-widget setting (system drive by default).
/// </summary>
public sealed class DiskSensor : ISensor
{
    public string Name => "Drives";
    public CardSize RequiredSize => CardSize.Medium;
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(30);

    public void Open()
    {
    }

    public void Sample(SystemSnapshot snapshot, in SamplingPlan plan) => snapshot.Drives = DriveCatalog.FixedDrives();

    public void Clear(SystemSnapshot snapshot) => snapshot.Drives = null;

    public void Close()
    {
    }
}

/// <summary>Fixed drives that are ready (BitLocker-locked or empty drives are skipped).</summary>
public static class DriveCatalog
{
    /// <summary>"C:" for the drive Windows is installed on.</summary>
    public static string SystemDriveName => (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").TrimEnd('\\');

    public static IReadOnlyList<DriveReading> FixedDrives()
    {
        string system = SystemDriveName;
        var result = new List<DriveReading>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                {
                    continue;
                }
                string name = drive.Name.TrimEnd('\\');
                long total = drive.TotalSize;
                long free = drive.TotalFreeSpace;
                result.Add(new DriveReading(name, drive.VolumeLabel ?? "",
                    string.Equals(name, system, StringComparison.OrdinalIgnoreCase),
                    (ulong)(total - free), (ulong)total));
            }
            catch (IOException)
            {
                // Drive went away or is not accessible right now; skip it this time.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return result;
    }
}
