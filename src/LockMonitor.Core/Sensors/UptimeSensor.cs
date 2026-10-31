using LockMonitor.Rendering;
using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>
/// Time since Windows started, like Task Manager's "Up time" (GetTickCount64: includes sleep).
/// With Fast Startup, "Shut down" is a hybrid hibernate and does not reset it; only Restart does.
/// </summary>
public sealed class UptimeSensor : ISensor
{
    public string Name => "Uptime";
    public CardSize RequiredSize => CardSize.Large;
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(30);

    public void Open()
    {
    }

    public void Sample(SystemSnapshot snapshot, in SamplingPlan plan) =>
        snapshot.Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

    public void Clear(SystemSnapshot snapshot) => snapshot.Uptime = null;

    public void Close()
    {
    }
}
