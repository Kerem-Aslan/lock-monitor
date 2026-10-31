using LockMonitor.Rendering;

namespace LockMonitor.Sampling;

/// <summary>What the sampler should do right now, derived from the pinned widgets.</summary>
public readonly record struct SamplingPlan(bool IsActive, CardSize Size, TimeSpan Interval)
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    public static SamplingPlan Idle => new(false, CardSize.Small, DefaultInterval);

    /// <summary>
    /// Active widgets decide: the largest active size picks which sensors run, and the shortest
    /// requested interval wins. Inactive widgets do not count at all, so with no active widget the
    /// sampler stops completely.
    /// </summary>
    public static SamplingPlan From(IEnumerable<(bool IsActive, CardSize Size, TimeSpan Interval)> widgets)
    {
        bool any = false;
        var size = CardSize.Small;
        var interval = TimeSpan.MaxValue;
        foreach (var w in widgets)
        {
            if (!w.IsActive)
            {
                continue;
            }
            any = true;
            if (w.Size > size)
            {
                size = w.Size;
            }
            var requested = w.Interval < MinInterval ? MinInterval : w.Interval;
            if (requested < interval)
            {
                interval = requested;
            }
        }
        return any ? new SamplingPlan(true, size, interval) : Idle;
    }
}
