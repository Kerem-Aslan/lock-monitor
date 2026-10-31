using LockMonitor.Sensors;

namespace LockMonitor.Sampling;

/// <summary>
/// Runs every sensor from one background loop.
/// <list type="bullet">
/// <item>Idle (no active widget): the loop waits on a signal with no timeout, every sensor is
/// closed, so the provider does no polling at all.</item>
/// <item>Active: sensors needed by the largest active card size are sampled every
/// <see cref="SamplingPlan.Interval"/>; sensors no longer needed are closed.</item>
/// <item>A plan change (activate, resize, interval change) wakes the loop immediately.</item>
/// </list>
/// Sensors are only touched from the loop, so they need no locking of their own.
/// </summary>
public sealed class SamplingScheduler : IAsyncDisposable
{
    /// <summary>Delay between the priming sample (rate counters need two readings) and the first published one.</summary>
    public static readonly TimeSpan PrimeDelay = TimeSpan.FromMilliseconds(750);

    private readonly SensorRunner[] _runners;
    private readonly Action<SystemSnapshot> _publish;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _gate = new();
    private readonly Task _loop;

    private SamplingPlan _plan = SamplingPlan.Idle;
    private SystemSnapshot? _latest;

    public SamplingScheduler(IEnumerable<ISensor> sensors, Action<SystemSnapshot> publish, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _runners = [.. sensors.Select(s => new SensorRunner(s, _time))];
        _publish = publish;
        _loop = Task.Run(RunAsync);
    }

    /// <summary>Most recent published snapshot, or null before the first one.</summary>
    public SystemSnapshot? Latest => Volatile.Read(ref _latest);

    public SamplingPlan Plan
    {
        get
        {
            lock (_gate)
            {
                return _plan;
            }
        }
    }

    /// <summary>Number of samples taken since start; used by tests and diagnostics.</summary>
    public long TickCount => Interlocked.Read(ref _tickCount);
    private long _tickCount;

    public void Apply(SamplingPlan plan)
    {
        lock (_gate)
        {
            if (plan == _plan)
            {
                return;
            }
            _plan = plan;
        }
        _wake.Release();
    }

    private async Task RunAsync()
    {
        var token = _shutdown.Token;
        while (!token.IsCancellationRequested)
        {
            var plan = Plan;
            try
            {
                if (!plan.IsActive)
                {
                    CloseAll();
                    await _wake.WaitAsync(token).ConfigureAwait(false); // no timeout: zero polling while idle
                    DrainWakeSignals();
                    continue;
                }

                // A sensor opened on this tick (activation, larger card, GPU temperature switched on)
                // may be a rate counter that needs a second reading: sample, wait briefly, sample again.
                bool priming = _runners.Any(r => r.Sensor.IsNeededFor(plan) && !r.IsOpen && !r.IsBackingOff);
                var snapshot = Sample(plan);
                if (priming)
                {
                    await WaitAsync(PrimeDelay, token).ConfigureAwait(false);
                    continue;
                }

                Volatile.Write(ref _latest, snapshot);
                try
                {
                    _publish(snapshot);
                }
                catch (Exception ex)
                {
                    Log.Error("Publishing a snapshot failed", ex);
                }
                await WaitAsync(plan.Interval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Should not happen (runners isolate sensors), but never let the loop die.
                Log.Error("Sampling loop error", ex);
                await Task.Delay(plan.Interval, _time, token).ConfigureAwait(false);
            }
        }
        CloseAll();
    }

    private SystemSnapshot Sample(SamplingPlan plan)
    {
        var snapshot = Latest?.Clone() ?? new SystemSnapshot();
        snapshot.Timestamp = _time.GetUtcNow();
        foreach (var runner in _runners)
        {
            if (runner.Sensor.IsNeededFor(plan))
            {
                runner.Sample(snapshot, plan);
            }
            else
            {
                runner.Close();
                runner.Sensor.Clear(snapshot);
            }
        }
        Interlocked.Increment(ref _tickCount);
        return snapshot;
    }

    /// <summary>Waits for the interval or until the plan changes.</summary>
    private async Task WaitAsync(TimeSpan delay, CancellationToken token)
    {
        if (await _wake.WaitAsync(delay, token).ConfigureAwait(false))
        {
            DrainWakeSignals();
        }
    }

    private void DrainWakeSignals()
    {
        while (_wake.Wait(0))
        {
        }
    }

    private void CloseAll()
    {
        foreach (var runner in _runners)
        {
            runner.Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _shutdown.Dispose();
        _wake.Dispose();
    }
}
