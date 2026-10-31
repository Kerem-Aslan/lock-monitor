using System.Globalization;
using System.Runtime.InteropServices;

namespace LockMonitor.Sensors;

/// <summary>
/// Turns "GPU Engine" counter instances into one usage figure per adapter, like Task Manager:
/// utilization is summed over all processes per (adapter, engine type), and the adapter's usage
/// is the busiest engine type ("3D", "Copy", "VideoDecode", "Compute_0", ...), capped at 100 %.
/// </summary>
internal sealed class GpuEngineAggregator
{
    private readonly Dictionary<string, int> _engineTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _engineTypeLookup;
    private readonly Dictionary<(long Luid, int EngineType), double> _sums = [];

    public GpuEngineAggregator()
    {
        _engineTypeLookup = _engineTypes.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public void Reset() => _sums.Clear();

    public void Add(ReadOnlySpan<char> instanceName, double utilization)
    {
        if (!TryParseInstance(instanceName, out long luid, out var engineType) || !double.IsFinite(utilization))
        {
            return;
        }
        if (!_engineTypeLookup.TryGetValue(engineType, out int engineId))
        {
            engineId = _engineTypes.Count;
            _engineTypes.Add(engineType.ToString(), engineId);
        }
        ref double sum = ref CollectionsMarshal.GetValueRefOrAddDefault(_sums, (luid, engineId), out _);
        sum += utilization;
    }

    /// <summary>Usage of one adapter, or null when it has no engine instances in this sample.</summary>
    public double? UsageFor(long luid)
    {
        double? max = null;
        foreach (var (key, sum) in _sums)
        {
            if (key.Luid == luid && (max is null || sum > max))
            {
                max = sum;
            }
        }
        return max is { } m ? Math.Clamp(m, 0, 100) : null;
    }

    /// <summary>Usage of the busiest adapter, for when the adapter list is unknown.</summary>
    public double? MaxUsage()
    {
        double? max = null;
        foreach (var sum in _sums.Values)
        {
            if (max is null || sum > max)
            {
                max = sum;
            }
        }
        return max is { } m ? Math.Clamp(m, 0, 100) : null;
    }

    /// <summary>
    /// Parses "pid_1234_luid_0x00000000_0x00011D3C_phys_0_eng_0_engtype_3D".
    /// The LUID is written high part first.
    /// </summary>
    internal static bool TryParseInstance(ReadOnlySpan<char> name, out long luid, out ReadOnlySpan<char> engineType)
    {
        luid = 0;
        engineType = default;

        int luidAt = name.IndexOf("luid_0x", StringComparison.OrdinalIgnoreCase);
        int typeAt = name.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
        if (luidAt < 0 || typeAt < 0)
        {
            return false;
        }

        var rest = name[(luidAt + "luid_0x".Length)..];
        int split = rest.IndexOf("_0x", StringComparison.OrdinalIgnoreCase);
        if (split <= 0)
        {
            return false;
        }
        var highHex = rest[..split];
        rest = rest[(split + "_0x".Length)..];
        int end = rest.IndexOf('_');
        var lowHex = end < 0 ? rest : rest[..end];

        if (!uint.TryParse(highHex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint high) ||
            !uint.TryParse(lowHex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint low))
        {
            return false;
        }

        engineType = name[(typeAt + "engtype_".Length)..];
        if (engineType.IsEmpty)
        {
            return false;
        }
        luid = ((long)high << 32) | low;
        return true;
    }
}
