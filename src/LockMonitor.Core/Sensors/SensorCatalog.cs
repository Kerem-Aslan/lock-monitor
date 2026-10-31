namespace LockMonitor.Sensors;

/// <summary>Every sensor the widget uses, in display order.</summary>
public static class SensorCatalog
{
    public static ISensor[] CreateAll(GpuAdapterCatalog adapters) =>
    [
        new CpuUsageSensor(),
        new MemorySensor(),
        new GpuUsageSensor(adapters),
        new GpuMemorySensor(adapters),
        new GpuTemperatureSensor(adapters),
        new NetworkSensor(),
        new DiskSensor(),
        new ThermalZoneSensor(),
        new BatterySensor(),
        new UptimeSensor(),
        new TopProcessesSensor(),
    ];
}
