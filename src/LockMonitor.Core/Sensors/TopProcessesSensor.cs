using System.Runtime.InteropServices;
using LockMonitor.Native;
using LockMonitor.Rendering;
using LockMonitor.Sampling;

namespace LockMonitor.Sensors;

/// <summary>
/// The apps using the most CPU since the previous sample. CPU time of every process comes from one
/// NtQuerySystemInformation call (no process handles, works for elevated processes). Processes are
/// grouped by image name ("chrome" = all chrome.exe), and usage is normalized to all logical
/// processors, like Task Manager's Processes tab. Runs only while a large card is active.
/// </summary>
public sealed class TopProcessesSensor(TimeProvider? time = null) : ISensor
{
    public const int Count = 3;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ProcessCpuTracker _tracker = new(Environment.ProcessorCount);
    private nint _buffer;
    private uint _bufferSize;

    public string Name => "Top processes";
    public CardSize RequiredSize => CardSize.Large;

    /// <summary>
    /// The snapshot of ~600 processes and their threads costs ~10 ms per call on a busy desktop,
    /// far more than every other sensor together, and a top-3 list does not need second-by-second
    /// updates: refresh every 5 s (the list then shows the average over those 5 s).
    /// </summary>
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(5);

    public void Open()
    {
        _tracker.Reset();
        Read(); // baseline
    }

    public void Sample(SystemSnapshot snapshot, in SamplingPlan plan) => snapshot.TopProcesses = Read();

    public void Clear(SystemSnapshot snapshot) => snapshot.TopProcesses = null;

    public void Close()
    {
        _tracker.Reset();
        if (_buffer != 0)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = 0;
            _bufferSize = 0;
        }
    }

    private unsafe IReadOnlyList<ProcessUsage>? Read()
    {
        if (_buffer == 0)
        {
            _bufferSize = 512 * 1024;
            _buffer = Marshal.AllocHGlobal((nint)_bufferSize);
        }
        int status;
        while ((status = NtDll.NtQuerySystemInformation(NtDll.SystemProcessInformation, (void*)_buffer, _bufferSize, out uint needed))
               == NtDll.STATUS_INFO_LENGTH_MISMATCH)
        {
            _bufferSize = Math.Max(needed + needed / 4, _bufferSize * 2); // headroom for new processes
            _buffer = Marshal.ReAllocHGlobal(_buffer, (nint)_bufferSize);
        }
        if (status < 0)
        {
            throw new InvalidOperationException($"NtQuerySystemInformation failed with NTSTATUS 0x{status:X8}");
        }

        _tracker.BeginSample(_time.GetTimestamp(), _time.TimestampFrequency);
        byte* entry = (byte*)_buffer;
        while (true)
        {
            long pid = *(long*)(entry + NtDll.OffsetProcessId);
            if (pid != 0) // 0 = System Idle Process
            {
                long cpu = *(long*)(entry + NtDll.OffsetUserTime) + *(long*)(entry + NtDll.OffsetKernelTime);
                long created = *(long*)(entry + NtDll.OffsetCreateTime);
                int nameBytes = *(ushort*)(entry + NtDll.OffsetImageNameLength);
                char* namePtr = *(char**)(entry + NtDll.OffsetImageNameBuffer);
                var name = namePtr is null ? "System".AsSpan() : new ReadOnlySpan<char>(namePtr, nameBytes / 2);
                _tracker.Add(pid, created, cpu, name);
            }
            uint next = *(uint*)(entry + NtDll.OffsetNextEntry);
            if (next == 0)
            {
                break;
            }
            entry += next;
        }
        return _tracker.EndSample(Count);
    }
}

/// <summary>Turns cumulative per-process CPU times into per-app usage between two samples.</summary>
internal sealed class ProcessCpuTracker
{
    private readonly int _logicalProcessors;
    private Dictionary<(long Pid, long Created), long> _previous = [];
    private Dictionary<(long Pid, long Created), long> _current = [];
    private readonly Dictionary<string, double> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double>.AlternateLookup<ReadOnlySpan<char>> _byNameLookup;
    private long _previousTimestamp;
    private long _timestamp;
    private long _frequency;
    private bool _hasBaseline;

    public ProcessCpuTracker(int logicalProcessors)
    {
        _logicalProcessors = Math.Max(1, logicalProcessors);
        _byNameLookup = _byName.GetAlternateLookup<ReadOnlySpan<char>>(); // no string per process per tick
    }

    public void Reset()
    {
        _previous.Clear();
        _current.Clear();
        _hasBaseline = false;
    }

    public void BeginSample(long timestamp, long frequency)
    {
        _timestamp = timestamp;
        _frequency = frequency;
        _current.Clear();
        _byName.Clear();
    }

    /// <param name="cpuTime">User + kernel time in 100 ns units.</param>
    public void Add(long pid, long createTime, long cpuTime, ReadOnlySpan<char> imageName)
    {
        var key = (pid, createTime); // create time guards against PID reuse
        _current[key] = cpuTime;
        if (!_hasBaseline || !_previous.TryGetValue(key, out long before) || cpuTime <= before)
        {
            return; // new process: no delta yet
        }
        var name = imageName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? imageName[..^4] : imageName;
        if (_byNameLookup.TryGetValue(name, out double sum))
        {
            _byNameLookup[name] = sum + (cpuTime - before);
        }
        else
        {
            _byName[name.ToString()] = cpuTime - before;
        }
    }

    /// <summary>Busiest apps since the previous sample, or null for the first (baseline) sample.</summary>
    public IReadOnlyList<ProcessUsage>? EndSample(int count)
    {
        IReadOnlyList<ProcessUsage>? result = null;
        double elapsed100ns = (double)(_timestamp - _previousTimestamp) / _frequency * 10_000_000;
        if (_hasBaseline && elapsed100ns > 0)
        {
            double capacity = elapsed100ns * _logicalProcessors;
            result =
            [
                .. _byName
                    .OrderByDescending(p => p.Value)
                    .Take(count)
                    .Select(p => new ProcessUsage(p.Key, Math.Clamp(100.0 * p.Value / capacity, 0, 100))),
            ];
        }
        (_previous, _current) = (_current, _previous);
        _previousTimestamp = _timestamp;
        _hasBaseline = true;
        return result;
    }
}
