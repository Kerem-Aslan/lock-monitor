using LockMonitor.Native;
using LockMonitor.Rendering;
using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>
/// Total CPU usage the way Task Manager shows it: "% Processor Utility" (frequency-aware, can
/// exceed 100 % under turbo, so it is clamped). Falls back to "% Processor Time" on systems
/// without the Processor Information counter set.
/// </summary>
public sealed class CpuUsageSensor : ISensor
{
    private const string UtilityPath = @"\Processor Information(_Total)\% Processor Utility";
    private const string TimePath = @"\Processor(_Total)\% Processor Time";

    private PdhQuery? _query;
    private nint _counter;

    public string Name => "CPU usage";
    public CardSize RequiredSize => CardSize.Small;
    public TimeSpan RefreshInterval => TimeSpan.Zero;

    public void Open()
    {
        _query = new PdhQuery();
        try
        {
            _counter = _query.AddCounter(UtilityPath);
        }
        catch (PdhException)
        {
            _counter = _query.AddCounter(TimePath);
        }
        _query.Collect(); // rate counter: the first collection only sets the baseline
    }

    public void Sample(SystemSnapshot snapshot)
    {
        _query!.Collect();
        snapshot.CpuPercent = PdhQuery.GetDouble(_counter, noCap100: true) is { } value
            ? Math.Clamp(value, 0, 100)
            : null;
    }

    public void Clear(SystemSnapshot snapshot) => snapshot.CpuPercent = null;

    public void Close()
    {
        _query?.Dispose();
        _query = null;
        _counter = 0;
    }
}
