using System.Runtime.InteropServices;

namespace LockMonitor.Native;

/// <summary>
/// NtQuerySystemInformation(SystemProcessInformation): CPU times of every process in one call,
/// without opening process handles, so it works for elevated/protected processes too and needs no
/// admin rights. The function is documented in winternl.h; the field offsets below are the stable
/// x64/ARM64 layout of SYSTEM_PROCESS_INFORMATION used since Windows Vista.
/// </summary>
internal static unsafe partial class NtDll
{
    public const int SystemProcessInformation = 5;
    public const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

    // SYSTEM_PROCESS_INFORMATION (64-bit)
    public const int OffsetNextEntry = 0x00;      // ULONG NextEntryOffset
    public const int OffsetCreateTime = 0x20;     // LARGE_INTEGER CreateTime
    public const int OffsetUserTime = 0x28;       // LARGE_INTEGER UserTime (100 ns)
    public const int OffsetKernelTime = 0x30;     // LARGE_INTEGER KernelTime (100 ns)
    public const int OffsetImageNameLength = 0x38;// UNICODE_STRING.Length (bytes)
    public const int OffsetImageNameBuffer = 0x40;// UNICODE_STRING.Buffer
    public const int OffsetProcessId = 0x50;      // HANDLE UniqueProcessId

    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(int infoClass, void* buffer, uint length, out uint returnLength);
}
