using System.Runtime.InteropServices;

namespace LockMonitor.Native;

internal static partial class Ole32
{
    public const uint CLSCTX_LOCAL_SERVER = 0x4;
    public const uint REGCLS_MULTIPLEUSE = 0x1;

    [LibraryImport("ole32.dll")]
    public static partial int CoRegisterClassObject(in Guid rclsid, nint pUnk, uint dwClsContext, uint flags, out uint lpdwRegister);

    [LibraryImport("ole32.dll")]
    public static partial int CoRevokeClassObject(uint dwRegister);
}

internal static partial class Kernel32
{
    public const uint ATTACH_PARENT_PROCESS = unchecked((uint)-1);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(uint dwProcessId);
}

internal static partial class User32
{
    public const uint MB_OK = 0x0;
    public const uint MB_ICONINFORMATION = 0x40;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBoxW(nint hWnd, string lpText, string lpCaption, uint uType);
}
