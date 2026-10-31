using LockMonitor.Native;
using LockMonitor.Rendering;
using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>
/// Download/upload speed as the change of the physical interfaces' 64-bit byte counters between
/// two samples (GetIfTable2). The first sample after opening only sets the baseline.
/// </summary>
public sealed class NetworkSensor(TimeProvider? time = null) : ISensor
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ByteRateCounter _received = new();
    private readonly ByteRateCounter _sent = new();

    public string Name => "Network";
    public CardSize RequiredSize => CardSize.Medium;
    public TimeSpan RefreshInterval => TimeSpan.Zero;

    public void Open()
    {
        _received.Reset();
        _sent.Reset();
        var (received, sent, _) = IpHlpApi.ReadTotals();
        long now = _time.GetTimestamp();
        _received.Update(received, now, _time.TimestampFrequency);
        _sent.Update(sent, now, _time.TimestampFrequency);
    }

    public void Sample(SystemSnapshot snapshot, in SamplingPlan plan)
    {
        var (received, sent, _) = IpHlpApi.ReadTotals();
        long now = _time.GetTimestamp();
        snapshot.NetworkDownBytesPerSecond = _received.Update(received, now, _time.TimestampFrequency);
        snapshot.NetworkUpBytesPerSecond = _sent.Update(sent, now, _time.TimestampFrequency);
    }

    public void Clear(SystemSnapshot snapshot)
    {
        snapshot.NetworkDownBytesPerSecond = null;
        snapshot.NetworkUpBytesPerSecond = null;
    }

    public void Close()
    {
        _received.Reset();
        _sent.Reset();
    }
}

/// <summary>Turns a monotonically increasing byte counter into a rate.</summary>
public sealed class ByteRateCounter
{
    private ulong? _lastValue;
    private long _lastTimestamp;

    public void Reset() => _lastValue = null;

    /// <summary>
    /// Records a reading and returns bytes per second since the previous one, or null for the
    /// first reading. A counter that went backwards (adapter reset/removed) restarts the baseline.
    /// </summary>
    public double? Update(ulong value, long timestamp, long timestampFrequency)
    {
        double? rate = null;
        if (_lastValue is { } last && value >= last && timestamp > _lastTimestamp)
        {
            double seconds = (double)(timestamp - _lastTimestamp) / timestampFrequency;
            rate = (value - last) / seconds;
        }
        _lastValue = value;
        _lastTimestamp = timestamp;
        return rate;
    }
}
