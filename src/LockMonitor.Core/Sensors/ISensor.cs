using LockMonitor.Rendering;
using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>
/// One independent metric source. Sensors are opened only while a widget that needs them is
/// active, and closed again when no active widget needs them, so an idle provider holds no
/// counters or handles.
/// </summary>
public interface ISensor
{
    string Name { get; }

    /// <summary>Smallest card size that displays this sensor's values.</summary>
    CardSize RequiredSize { get; }

    /// <summary>Minimum time between refreshes; <see cref="TimeSpan.Zero"/> means every tick.</summary>
    TimeSpan RefreshInterval { get; }

    /// <summary>Allocates handles/counters. May throw; the runner then retries later.</summary>
    void Open();

    /// <summary>Writes this sensor's fields into the snapshot. May throw.</summary>
    void Sample(SystemSnapshot snapshot);

    /// <summary>Marks this sensor's fields as not available.</summary>
    void Clear(SystemSnapshot snapshot);

    /// <summary>Releases everything <see cref="Open"/> allocated. Must not throw.</summary>
    void Close();
}
