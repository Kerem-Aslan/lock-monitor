namespace LockMonitor.Sensors;

/// <summary>Every sensor the widget uses, in display order.</summary>
public static class SensorCatalog
{
    public static ISensor[] CreateAll() =>
    [
        new CpuUsageSensor(),
        new MemorySensor(),
        new GpuUsageSensor(),
    ];
}
