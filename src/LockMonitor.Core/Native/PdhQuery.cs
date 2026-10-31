using System.Runtime.InteropServices;

namespace LockMonitor.Native;

/// <summary>
/// One PDH query handle. Each sensor owns its own query so a failing counter cannot affect
/// other sensors. Closing the query releases every counter it holds.
/// </summary>
public sealed class PdhQuery : IDisposable
{
    private nint _query;
    private nint _arrayBuffer;
    private uint _arrayBufferSize;

    public PdhQuery()
    {
        Check(Pdh.PdhOpenQuery(null, 0, out _query), "PdhOpenQuery");
    }

    public nint AddCounter(string englishPath)
    {
        Check(Pdh.PdhAddEnglishCounter(_query, englishPath, 0, out var counter), $"PdhAddEnglishCounter({englishPath})");
        return counter;
    }

    /// <summary>Collects a new raw sample. Rate counters need two collections before they have a value.</summary>
    public void Collect() => Check(Pdh.PdhCollectQueryData(_query), "PdhCollectQueryData");

    /// <summary>Returns the formatted value, or null while the counter has no valid data yet.</summary>
    public static double? GetDouble(nint counter, bool noCap100 = false)
    {
        uint format = Pdh.PDH_FMT_DOUBLE | (noCap100 ? Pdh.PDH_FMT_NOCAP100 : 0);
        uint status = Pdh.PdhGetFormattedCounterValue(counter, format, out _, out var value);
        return status == Pdh.ERROR_SUCCESS && Pdh.IsValid(value.CStatus) ? value.DoubleValue : null;
    }

    /// <summary>
    /// Visits every instance of a wildcard counter without allocating strings: the instance name is
    /// handed out as a span over PDH's own buffer, valid only during the callback.
    /// The buffer is reused between calls.
    /// </summary>
    public unsafe int VisitArray(nint counter, InstanceVisitor visitor)
    {
        const uint format = Pdh.PDH_FMT_DOUBLE | Pdh.PDH_FMT_NOCAP100;
        uint size = _arrayBufferSize;
        uint status = Pdh.PdhGetFormattedCounterArray(counter, format, ref size, out uint count, _arrayBuffer);
        if (status == Pdh.PDH_MORE_DATA)
        {
            // Grow with headroom so instance churn (processes starting) rarely forces a reallocation.
            uint newSize = size + size / 4;
            _arrayBuffer = _arrayBuffer == 0 ? Marshal.AllocHGlobal((nint)newSize) : Marshal.ReAllocHGlobal(_arrayBuffer, (nint)newSize);
            _arrayBufferSize = newSize;
            size = newSize;
            status = Pdh.PdhGetFormattedCounterArray(counter, format, ref size, out count, _arrayBuffer);
        }
        if (status != Pdh.ERROR_SUCCESS)
        {
            // No valid data yet (first collection of a rate counter) is not an error.
            return 0;
        }

        var items = (Pdh.PDH_FMT_COUNTERVALUE_ITEM_W*)_arrayBuffer;
        int visited = 0;
        for (uint i = 0; i < count; i++)
        {
            if (!Pdh.IsValid(items[i].Value.CStatus))
            {
                continue;
            }
            var name = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(items[i].Name);
            visitor(name, items[i].Value.DoubleValue);
            visited++;
        }
        return visited;
    }

    public delegate void InstanceVisitor(ReadOnlySpan<char> instanceName, double value);

    public void Dispose()
    {
        if (_query != 0)
        {
            Pdh.PdhCloseQuery(_query);
            _query = 0;
        }
        if (_arrayBuffer != 0)
        {
            Marshal.FreeHGlobal(_arrayBuffer);
            _arrayBuffer = 0;
            _arrayBufferSize = 0;
        }
    }

    private static void Check(uint status, string operation)
    {
        if (status != Pdh.ERROR_SUCCESS)
        {
            throw new PdhException(operation, status);
        }
    }
}
