using System.Runtime.InteropServices;

namespace LockMonitor.Native;

/// <summary>Performance Data Helper (pdh.dll). Readable by standard users in an interactive session.</summary>
internal static partial class Pdh
{
    public const uint ERROR_SUCCESS = 0;
    public const uint PDH_MORE_DATA = 0x800007D2;
    public const uint PDH_CSTATUS_VALID_DATA = 0x0;
    public const uint PDH_CSTATUS_NEW_DATA = 0x1;

    public const uint PDH_FMT_DOUBLE = 0x00000200;
    public const uint PDH_FMT_NOCAP100 = 0x00008000;

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhOpenQuery(string? dataSource, nint userData, out nint query);

    /// <summary>Adds a counter by its English path, so it works on localized Windows too.</summary>
    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhAddEnglishCounter(nint query, string fullCounterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    public static partial uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint bufferSize, out uint itemCount, nint itemBuffer);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCloseQuery(nint query);

    public static bool IsValid(uint cstatus) => cstatus is PDH_CSTATUS_VALID_DATA or PDH_CSTATUS_NEW_DATA;

    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE
    {
        public uint CStatus;
        public double DoubleValue; // union { LONG; double; LONGLONG; LPCWSTR }, 8-byte aligned
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PDH_FMT_COUNTERVALUE_ITEM_W
    {
        public char* Name;
        public PDH_FMT_COUNTERVALUE Value;
    }
}

/// <summary>Thrown when a PDH call fails; carries the PDH status code.</summary>
public sealed class PdhException(string operation, uint status)
    : Exception($"{operation} failed with PDH status 0x{status:X8}")
{
    public uint Status { get; } = status;
}
