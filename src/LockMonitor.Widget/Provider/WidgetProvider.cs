using LockMonitor.Rendering;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace LockMonitor.Provider;

/// <summary>
/// The object the widget host talks to over COM. One shared instance serves every pinned widget;
/// widgets are tracked by their host-assigned id. Host callbacks can arrive concurrently on
/// different RPC threads, so all state changes go through <see cref="_gate"/>.
/// </summary>
/// <remarks>
/// <see cref="WinRT.GeneratedWinRTExposedTypeAttribute"/> asks the CsWinRT source generator for the
/// WinRT vtables of this class. Without it, Native AOT builds answer E_NOINTERFACE for
/// IWidgetProvider: CsWinRT only auto-detects such classes in WinUI/UWP projects.
/// </remarks>
[WinRT.GeneratedWinRTExposedType]
internal sealed partial class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    public const string SystemMonitorDefinitionId = "SystemMonitor";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, WidgetSession> _sessions = new(StringComparer.Ordinal);
    private readonly ServerLifetime _lifetime;
    private readonly TemplateStore _templates;

    public WidgetProvider(ServerLifetime lifetime, TemplateStore templates)
    {
        _lifetime = lifetime;
        _templates = templates;
    }

    /// <summary>
    /// Restores widgets that were pinned before this process started. The host keeps them across
    /// provider restarts, together with their CustomState.
    /// Call this before the class object is registered; host callbacks that still race with it
    /// win, because recovery never overwrites a session that already exists.
    /// </summary>
    public void RecoverRunningWidgets()
    {
        var manager = WidgetManager.GetDefault();
        // GetWidgetInfos returns null (not an empty array) when no widget is pinned.
        foreach (var info in manager.GetWidgetInfos() ?? [])
        {
            var context = info.WidgetContext;
            if (context.DefinitionId != SystemMonitorDefinitionId)
            {
                Log.Info($"Deleting unknown widget {context.Id} ({context.DefinitionId})");
                manager.DeleteWidget(context.Id);
                continue;
            }

            bool added;
            lock (_gate)
            {
                added = _sessions.TryAdd(context.Id, new WidgetSession(context.Id, context.DefinitionId)
                {
                    Size = context.Size,
                    IsActive = context.IsActive,
                    CustomState = info.CustomState ?? string.Empty,
                });
            }
            Log.Info(added
                ? $"Recovered widget {context.Id} size={context.Size} active={context.IsActive}"
                : $"Widget {context.Id} already known (host callback arrived first)");
        }

        int count;
        lock (_gate)
        {
            count = _sessions.Count;
        }
        _lifetime.OnWidgetCountChanged(count);

        foreach (var session in Snapshot())
        {
            if (session.IsActive)
            {
                Push(session);
            }
        }
    }

    // ----- IWidgetProvider -------------------------------------------------------------------

    public void CreateWidget(WidgetContext widgetContext)
    {
        Log.Info($"CreateWidget {widgetContext.Id} def={widgetContext.DefinitionId} size={widgetContext.Size}");
        var session = new WidgetSession(widgetContext.Id, widgetContext.DefinitionId)
        {
            Size = widgetContext.Size,
            // A newly created widget starts in the active state (IWidgetProvider.Activate docs).
            IsActive = true,
        };

        int count;
        lock (_gate)
        {
            _sessions[session.Id] = session;
            count = _sessions.Count;
        }
        _lifetime.OnWidgetCountChanged(count);
        Push(session);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        Log.Info($"DeleteWidget {widgetId}");
        int count;
        lock (_gate)
        {
            _sessions.Remove(widgetId);
            count = _sessions.Count;
        }
        _lifetime.OnWidgetCountChanged(count);
    }

    public void Activate(WidgetContext widgetContext)
    {
        Log.Info($"Activate {widgetContext.Id} size={widgetContext.Size}");
        var session = GetOrAdd(widgetContext);
        lock (_gate)
        {
            session.IsActive = true;
            session.Size = widgetContext.Size;
            session.NeedsTemplate = true;
        }
        Push(session);
    }

    public void Deactivate(string widgetId)
    {
        Log.Info($"Deactivate {widgetId}");
        lock (_gate)
        {
            if (_sessions.TryGetValue(widgetId, out var session))
            {
                session.IsActive = false;
            }
        }
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        Log.Info($"OnActionInvoked {actionInvokedArgs.WidgetContext.Id} verb={actionInvokedArgs.Verb}");
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        var context = contextChangedArgs.WidgetContext;
        Log.Info($"OnWidgetContextChanged {context.Id} size={context.Size} active={context.IsActive}");
        var session = GetOrAdd(context);
        lock (_gate)
        {
            session.Size = context.Size;
            session.IsActive = context.IsActive;
            session.NeedsTemplate = true;
        }
        if (session.IsActive)
        {
            Push(session);
        }
    }

    // ----- IWidgetProvider2 ------------------------------------------------------------------

    public void OnCustomizationRequested(WidgetCustomizationRequestedArgs customizationRequestedArgs)
    {
        // Customization (update interval) arrives in phase 3; the definition is not customizable yet.
        Log.Info($"OnCustomizationRequested {customizationRequestedArgs.WidgetContext.Id}");
    }

    // ----- helpers ---------------------------------------------------------------------------

    private WidgetSession GetOrAdd(WidgetContext context)
    {
        bool added = false;
        WidgetSession? session;
        int count;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(context.Id, out session))
            {
                // The host can activate a widget this process has not seen (e.g. after a restart
                // that raced with recovery). Track it instead of dropping the call.
                session = new WidgetSession(context.Id, context.DefinitionId) { Size = context.Size };
                _sessions[context.Id] = session;
                added = true;
            }
            count = _sessions.Count;
        }
        if (added)
        {
            _lifetime.OnWidgetCountChanged(count);
        }
        return session;
    }

    private List<WidgetSession> Snapshot()
    {
        lock (_gate)
        {
            return [.. _sessions.Values];
        }
    }

    private void Push(WidgetSession session)
    {
        string data = BuildData(session);
        bool sendTemplate;
        lock (_gate)
        {
            sendTemplate = session.NeedsTemplate;
            if (!sendTemplate && data == session.LastData)
            {
                return; // nothing visible changed
            }
            session.NeedsTemplate = false;
            session.LastData = data;
        }

        try
        {
            var options = new WidgetUpdateRequestOptions(session.Id)
            {
                Data = data,
                CustomState = session.CustomState,
            };
            if (sendTemplate)
            {
                options.Template = _templates.Composed;
            }
            WidgetManager.GetDefault().UpdateWidget(options);
        }
        catch (Exception ex)
        {
            Log.Error($"UpdateWidget failed for {session.Id}", ex);
            lock (_gate)
            {
                session.NeedsTemplate = true;
                session.LastData = null;
            }
        }
    }

    // Phase 1: fixed values so the skeleton can be verified on the Widgets Board and lock screen.
    private static string BuildData(WidgetSession session) => CardData.Build(
    [
        new CardData.Metric("CPU", 23),
        new CardData.Metric("RAM", 47),
        new CardData.Metric("GPU", 91),
    ]);
}
