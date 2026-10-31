using LockMonitor.Native;
using LockMonitor.Rendering;
using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>
/// "System temp (ACPI)": the "Thermal Zone Information" performance counter, the only temperature
/// source Windows offers standard users without a driver. It is an ACPI thermal zone defined by the
/// PC's firmware, NOT the CPU die/package temperature, and many desktops expose no zone or a fixed
/// placeholder. Hence: hottest valid zone, and hidden when the value never changes for 5 minutes.
/// </summary>
public sealed class ThermalZoneSensor(TimeProvider? time = null) : ISensor
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly StaticValueDetector _detector = new(TimeSpan.FromMinutes(5));
    private PdhQuery.InstanceVisitor? _visitor;
    private PdhQuery? _query;
    private nint _counter;
    private bool _highPrecision;
    private double? _hottest;

    public string Name => "System temperature";
    public CardSize RequiredSize => CardSize.Large;
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(5);

    public void Open()
    {
        _visitor ??= (_, value) =>
        {
            double celsius = KelvinToCelsius(value, _highPrecision);
            if (IsPlausible(celsius) && (_hottest is null || celsius > _hottest))
            {
                _hottest = celsius;
            }
        };
        _query = new PdhQuery();
        try
        {
            _counter = _query.AddCounter(@"\Thermal Zone Information(*)\High Precision Temperature"); // tenths of a kelvin
            _highPrecision = true;
        }
        catch (PdhException)
        {
            _counter = _query.AddCounter(@"\Thermal Zone Information(*)\Temperature"); // kelvin
            _highPrecision = false;
        }
    }

    public void Sample(SystemSnapshot snapshot, in SamplingPlan plan)
    {
        _query!.Collect();
        _hottest = null;
        _query.VisitArray(_counter, _visitor!);

        snapshot.SystemTemperatureCelsius = _hottest is { } t && !_detector.IsStatic(t, _time.GetUtcNow()) ? t : null;
    }

    public void Clear(SystemSnapshot snapshot) => snapshot.SystemTemperatureCelsius = null;

    public void Close()
    {
        // The static-value history is kept: the board is often open for seconds only, so the
        // 5 minutes are counted across openings while the provider process lives.
        _query?.Dispose();
        _query = null;
    }

    internal static double KelvinToCelsius(double value, bool tenths) => (tenths ? value / 10.0 : value) - 273.15;

    /// <summary>Rejects 0 K and other garbage some firmware reports.</summary>
    internal static bool IsPlausible(double celsius) => celsius is > 1 and < 150;
}

/// <summary>
/// Flags a reading as a placeholder when it has not changed at all for a while.
/// Live thermal zones move by at least 0.1 K within minutes.
/// </summary>
public sealed class StaticValueDetector(TimeSpan window)
{
    private double? _first;
    private DateTimeOffset _since;
    private bool _changed;

    public bool IsStatic(double value, DateTimeOffset now)
    {
        if (_first is null)
        {
            _first = value;
            _since = now;
            return false;
        }
        if (Math.Abs(value - _first.Value) >= 0.05)
        {
            _changed = true;
        }
        return !_changed && now - _since >= window;
    }
}
