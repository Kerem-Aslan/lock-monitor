using LockMonitor.Rendering;

namespace LockMonitor.Sampling;

/// <summary>What one pinned widget needs from the sampler.</summary>
/// <param name="IsActive">The host is showing the widget right now.</param>
/// <param name="Size">Card size; larger cards need more sensors.</param>
/// <param name="Interval">Requested update interval.</param>
/// <param name="TemperatureLuid">Adapter whose temperature this widget shows, or null when the
/// widget does not show a GPU temperature (setting off, or small card).</param>
public readonly record struct WidgetDemand(bool IsActive, CardSize Size, TimeSpan Interval, long? TemperatureLuid = null);

/// <summary>What the sampler should do right now, derived from the pinned widgets.</summary>
public readonly record struct SamplingPlan(bool IsActive, CardSize Size, TimeSpan Interval, LuidSet TemperatureLuids = default)
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    public static SamplingPlan Idle => new(false, CardSize.Small, DefaultInterval);

    /// <summary>
    /// Active widgets decide: the largest active size picks which sensors run, the shortest
    /// requested interval wins, and GPU temperature is read only for adapters an active
    /// medium/large widget shows it for. Inactive widgets do not count, so with no active widget
    /// the sampler stops completely.
    /// </summary>
    public static SamplingPlan From(IEnumerable<WidgetDemand> widgets)
    {
        bool any = false;
        var size = CardSize.Small;
        var interval = TimeSpan.MaxValue;
        var temperature = new List<long>();
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
            if (w.TemperatureLuid is { } luid && w.Size >= CardSize.Medium)
            {
                temperature.Add(luid);
            }
        }
        return any ? new SamplingPlan(true, size, interval, new LuidSet(temperature)) : Idle;
    }
}

/// <summary>Small immutable set of adapter LUIDs with value equality.</summary>
public readonly struct LuidSet : IEquatable<LuidSet>
{
    private readonly long[]? _items;

    public LuidSet(IEnumerable<long> luids)
    {
        var sorted = luids.Distinct().Order().ToArray();
        _items = sorted.Length == 0 ? null : sorted;
    }

    public bool IsEmpty => _items is null;
    public ReadOnlySpan<long> Items => _items;
    public bool Contains(long luid) => _items is not null && Array.BinarySearch(_items, luid) >= 0;

    public bool Equals(LuidSet other) => Items.SequenceEqual(other.Items);
    public override bool Equals(object? obj) => obj is LuidSet other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var luid in Items)
        {
            hash.Add(luid);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(LuidSet left, LuidSet right) => left.Equals(right);
    public static bool operator !=(LuidSet left, LuidSet right) => !left.Equals(right);

    public override string ToString() => IsEmpty ? "none" : string.Join(",", _items!.Select(l => $"0x{l:X}"));
}
