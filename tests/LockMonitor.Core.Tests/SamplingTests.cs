using LockMonitor.Rendering;
using LockMonitor.Sampling;
using LockMonitor.Sensors;
using Xunit;

namespace LockMonitor.Tests;

public class SamplingPlanTests
{
    private static readonly TimeSpan TwoSeconds = TimeSpan.FromSeconds(2);

    [Fact]
    public void No_active_widget_means_idle()
    {
        var plan = SamplingPlan.From([(false, CardSize.Large, TwoSeconds), (false, CardSize.Small, TwoSeconds)]);
        Assert.False(plan.IsActive);
        Assert.Equal(SamplingPlan.Idle, SamplingPlan.From([]));
    }

    [Fact]
    public void Largest_active_size_and_shortest_interval_win_inactive_widgets_ignored()
    {
        var plan = SamplingPlan.From(
        [
            (true, CardSize.Small, TimeSpan.FromSeconds(5)),
            (true, CardSize.Medium, TwoSeconds),
            (false, CardSize.Large, TimeSpan.FromSeconds(1)),
        ]);
        Assert.Equal(new SamplingPlan(true, CardSize.Medium, TwoSeconds), plan);
    }

    [Fact]
    public void Interval_is_clamped_to_the_minimum()
    {
        var plan = SamplingPlan.From([(true, CardSize.Small, TimeSpan.FromMilliseconds(100))]);
        Assert.Equal(SamplingPlan.MinInterval, plan.Interval);
    }
}

public class SensorRunnerTests
{
    [Fact]
    public void Failure_clears_fields_and_does_not_throw()
    {
        var sensor = new FakeSensor { Fail = true };
        var runner = new SensorRunner(sensor, new ManualTime());
        var snapshot = new SystemSnapshot { CpuPercent = 50 };

        runner.Sample(snapshot);

        Assert.Null(snapshot.CpuPercent);
    }

    [Fact]
    public void Repeated_failures_close_the_sensor_and_back_off_then_retry()
    {
        var time = new ManualTime();
        var sensor = new FakeSensor { Fail = true };
        var runner = new SensorRunner(sensor, time);

        for (int i = 0; i < SensorRunner.FailuresBeforeBackoff; i++)
        {
            runner.Sample(new SystemSnapshot());
        }
        Assert.True(runner.IsBackingOff);
        Assert.False(runner.IsOpen);
        int samplesBefore = sensor.SampleCalls;

        runner.Sample(new SystemSnapshot()); // still backing off: sensor not touched
        Assert.Equal(samplesBefore, sensor.SampleCalls);

        sensor.Fail = false;
        time.Advance(SensorRunner.InitialBackoff);
        var snapshot = new SystemSnapshot();
        runner.Sample(snapshot);
        Assert.Equal(42, snapshot.CpuPercent);
        Assert.True(runner.IsOpen);
    }

    [Fact]
    public void Slow_sensors_keep_their_value_until_the_refresh_interval_passes()
    {
        var time = new ManualTime();
        var sensor = new FakeSensor { Refresh = TimeSpan.FromSeconds(30) };
        var runner = new SensorRunner(sensor, time);

        runner.Sample(new SystemSnapshot());
        runner.Sample(new SystemSnapshot());
        Assert.Equal(1, sensor.SampleCalls);

        time.Advance(TimeSpan.FromSeconds(30));
        runner.Sample(new SystemSnapshot());
        Assert.Equal(2, sensor.SampleCalls);
    }
}

public class SamplingSchedulerTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task Idle_scheduler_never_samples_or_opens_sensors()
    {
        var sensor = new FakeSensor();
        await using var scheduler = new SamplingScheduler([sensor], _ => { });

        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.Equal(0, scheduler.TickCount);
        Assert.Equal(0, sensor.OpenCalls);
    }

    [Fact]
    public async Task Active_plan_publishes_and_going_idle_closes_sensors()
    {
        var sensor = new FakeSensor();
        var published = new List<SystemSnapshot>();
        await using var scheduler = new SamplingScheduler([sensor], s => { lock (published) published.Add(s); });

        scheduler.Apply(new SamplingPlan(true, CardSize.Small, Fast));
        await WaitUntil(() => { lock (published) return published.Count >= 3; });
        Assert.Equal(42, scheduler.Latest!.CpuPercent);
        Assert.True(sensor.IsOpen);

        scheduler.Apply(SamplingPlan.Idle);
        await WaitUntil(() => !sensor.IsOpen);
        long ticks = scheduler.TickCount;
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Equal(ticks, scheduler.TickCount); // zero polling while idle
    }

    [Fact]
    public async Task A_failing_sensor_does_not_stop_the_others()
    {
        var broken = new FakeSensor { Fail = true };
        var memory = new FakeSensor { Field = Field.Memory };
        SystemSnapshot? last = null;
        await using var scheduler = new SamplingScheduler([broken, memory], s => Volatile.Write(ref last, s));

        scheduler.Apply(new SamplingPlan(true, CardSize.Small, Fast));
        await WaitUntil(() => Volatile.Read(ref last) is not null && scheduler.TickCount >= 5);

        Assert.Null(last!.CpuPercent);
        Assert.Equal(42, last.MemoryPercent);
    }

    [Fact]
    public async Task Sensors_for_larger_cards_run_only_while_such_a_card_is_active()
    {
        var small = new FakeSensor();
        var large = new FakeSensor { Size = CardSize.Large, Field = Field.Memory };
        await using var scheduler = new SamplingScheduler([small, large], _ => { });

        scheduler.Apply(new SamplingPlan(true, CardSize.Small, Fast));
        await WaitUntil(() => scheduler.TickCount >= 3);
        Assert.Equal(0, large.OpenCalls);

        scheduler.Apply(new SamplingPlan(true, CardSize.Large, Fast));
        await WaitUntil(() => large.IsOpen);

        scheduler.Apply(new SamplingPlan(true, CardSize.Small, Fast));
        await WaitUntil(() => !large.IsOpen);
        Assert.True(small.IsOpen);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the scheduler");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}

internal enum Field { Cpu, Memory }

internal sealed class FakeSensor : ISensor
{
    public bool Fail { get; set; }
    public TimeSpan Refresh { get; set; } = TimeSpan.Zero;
    public CardSize Size { get; set; } = CardSize.Small;
    public Field Field { get; set; } = Field.Cpu;
    public int OpenCalls;
    public int SampleCalls;
    public volatile bool IsOpen;

    public string Name => $"fake {Field}";
    public CardSize RequiredSize => Size;
    public TimeSpan RefreshInterval => Refresh;

    public void Open()
    {
        Interlocked.Increment(ref OpenCalls);
        IsOpen = true;
    }

    public void Sample(SystemSnapshot snapshot)
    {
        Interlocked.Increment(ref SampleCalls);
        if (Fail)
        {
            throw new InvalidOperationException("sensor broken");
        }
        if (Field == Field.Cpu)
        {
            snapshot.CpuPercent = 42;
        }
        else
        {
            snapshot.MemoryPercent = 42;
        }
    }

    public void Clear(SystemSnapshot snapshot)
    {
        if (Field == Field.Cpu)
        {
            snapshot.CpuPercent = null;
        }
        else
        {
            snapshot.MemoryPercent = null;
        }
    }

    public void Close() => IsOpen = false;
}

internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
