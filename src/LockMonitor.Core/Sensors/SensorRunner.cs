using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>
/// Isolates one sensor: exceptions never escape, the sensor's fields become N/A, and after
/// repeated failures the sensor is closed and retried with exponential back-off. Other sensors
/// keep working either way.
/// </summary>
public sealed class SensorRunner(ISensor sensor, TimeProvider time)
{
    public const int FailuresBeforeBackoff = 3;
    public static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

    private bool _open;
    private int _consecutiveFailures;
    private int _backoffLevel;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextRefresh = DateTimeOffset.MinValue;

    public ISensor Sensor => sensor;
    public bool IsOpen => _open;
    public bool IsBackingOff => time.GetUtcNow() < _retryAt;

    public void Sample(SystemSnapshot snapshot)
    {
        var now = time.GetUtcNow();
        if (now < _retryAt)
        {
            sensor.Clear(snapshot);
            return;
        }
        if (_open && now < _nextRefresh)
        {
            return; // keep the previous value copied into the snapshot
        }

        try
        {
            if (!_open)
            {
                sensor.Open();
                _open = true;
            }
            sensor.Sample(snapshot);
            _consecutiveFailures = 0;
            _backoffLevel = 0;
            _nextRefresh = now + sensor.RefreshInterval;
        }
        catch (Exception ex)
        {
            sensor.Clear(snapshot);
            _consecutiveFailures++;
            if (_consecutiveFailures == 1)
            {
                Log.Error($"Sensor '{sensor.Name}' failed", ex);
            }
            if (_consecutiveFailures >= FailuresBeforeBackoff)
            {
                var delay = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, InitialBackoff.Ticks << Math.Min(_backoffLevel, 10)));
                _backoffLevel++;
                _consecutiveFailures = 0;
                _retryAt = now + delay;
                Close();
                Log.Info($"Sensor '{sensor.Name}' disabled for {delay.TotalSeconds:0}s after {FailuresBeforeBackoff} failures");
            }
        }
    }

    public void Close()
    {
        if (!_open)
        {
            return;
        }
        _open = false;
        _nextRefresh = DateTimeOffset.MinValue;
        try
        {
            sensor.Close();
        }
        catch (Exception ex)
        {
            Log.Error($"Sensor '{sensor.Name}' failed to close", ex);
        }
    }
}
