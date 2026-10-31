using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace LockMonitor.Com;

/// <summary>Classic COM IClassFactory, declared with the source-generated COM interop so it works under Native AOT.</summary>
[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint pUnkOuter, in Guid riid, out nint ppvObject);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
}

/// <summary>
/// Hands the widget host our single, shared <see cref="IWidgetProvider"/> instance.
/// The host may call CreateInstance many times; every call gets the same object so all
/// widget state lives in one place.
/// </summary>
[GeneratedComClass]
internal sealed partial class WidgetProviderFactory(Func<IWidgetProvider> getProvider) : IClassFactory
{
    private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);

    public int CreateInstance(nint pUnkOuter, in Guid riid, out nint ppvObject)
    {
        ppvObject = 0;
        if (pUnkOuter != 0)
        {
            return CLASS_E_NOAGGREGATION;
        }

        nint pProvider = 0;
        try
        {
            pProvider = MarshalInspectable<IWidgetProvider>.FromManaged(getProvider());
            return Marshal.QueryInterface(pProvider, in riid, out ppvObject);
        }
        catch (Exception ex)
        {
            Log.Error("ClassFactory.CreateInstance failed", ex);
            return Marshal.GetHRForException(ex);
        }
        finally
        {
            if (pProvider != 0)
            {
                Marshal.Release(pProvider);
            }
        }
    }

    public int LockServer(bool fLock) => 0;
}
