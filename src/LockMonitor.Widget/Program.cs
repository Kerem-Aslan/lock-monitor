using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using LockMonitor.Com;
using LockMonitor.Native;
using LockMonitor.Provider;
using LockMonitor.Rendering;
using LockMonitor.Sensors;

namespace LockMonitor;

internal static class Program
{
    /// <summary>Must match com:Class Id and CreateInstance ClassId in Package.appxmanifest.</summary>
    private static readonly Guid ProviderClsid = new("A1202D97-A23F-4DFA-A3E1-C664383A2ECB");

    [MTAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Unhandled exception", e.ExceptionObject as Exception);

        if (HasArg(args, "-RegisterProcessAsComServer"))
        {
            return RunComServer();
        }

        ShowHowToAdd();
        return 0;
    }

    private static int RunComServer()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Log.Info($"COM server starting (pid {Environment.ProcessId}, {RuntimeInformation.FrameworkDescription})");

        using var lifetime = new ServerLifetime(TimeSpan.FromSeconds(30));
        var templates = TemplateStore.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Templates"));
        var provider = new WidgetProvider(lifetime, templates, SensorCatalog.CreateAll());
        var factory = new WidgetProviderFactory(() => provider);

        // Restore pinned widgets before the host can reach us, so recovery and host callbacks
        // (CreateWidget/Activate) do not race on the same widget.
        provider.RecoverRunningWidgets();

        var comWrappers = new StrategyBasedComWrappers();
        nint pFactory = comWrappers.GetOrCreateComInterfaceForObject(factory, CreateComInterfaceFlags.None);
        try
        {
            int hr = Ole32.CoRegisterClassObject(ProviderClsid, pFactory,
                Ole32.CLSCTX_LOCAL_SERVER, Ole32.REGCLS_MULTIPLEUSE, out uint cookie);
            if (hr < 0)
            {
                Log.Error($"CoRegisterClassObject failed: 0x{hr:X8}");
                return hr;
            }

            try
            {
                lifetime.WaitForExit();
            }
            finally
            {
                Ole32.CoRevokeClassObject(cookie);
            }
        }
        catch (Exception ex)
        {
            Log.Error("COM server crashed", ex);
            return Marshal.GetHRForException(ex);
        }
        finally
        {
            Marshal.Release(pFactory);
            GC.KeepAlive(factory);
        }

        provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Log.Info("COM server stopped");
        return 0;
    }

    private static void ShowHowToAdd()
    {
        const string text =
            "Lock Monitor runs as a widget.\n\n" +
            "Widgets board: press Win+W, select + (Add widgets) and pick \"System Monitor\".\n\n" +
            "Lock screen: Settings > Personalization > Lock screen > Widgets > Add widget.";
        User32.MessageBoxW(0, text, "Lock Monitor", User32.MB_OK | User32.MB_ICONINFORMATION);
    }

    private static bool HasArg(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
}
