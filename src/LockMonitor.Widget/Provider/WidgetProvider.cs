using System.Diagnostics;
using LockMonitor.Rendering;
using LockMonitor.Sampling;
using LockMonitor.Sensors;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;
using Windows.System.Power;

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
internal sealed partial class WidgetProvider : IWidgetProvider, IWidgetProvider2, IAsyncDisposable
{
    public const string SystemMonitorDefinitionId = "SystemMonitor";

    private const string SaveSettingsVerb = "saveSettings";
    private const string CancelSettingsVerb = "cancelSettings";

    /// <summary>
    /// A cached reading older than this is not shown when a widget becomes active. The board is
    /// often open for only a few seconds, so recent values beat "--" until the next sample (~1 s).
    /// </summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, WidgetSession> _sessions = new(StringComparer.Ordinal);
    private readonly ServerLifetime _lifetime;
    private readonly TemplateStore _templates;
    private readonly GpuAdapterCatalog _adapters;
    private readonly SamplingScheduler _scheduler;
    private bool _energySaverOn;

    public WidgetProvider(ServerLifetime lifetime, TemplateStore templates, GpuAdapterCatalog adapters, IEnumerable<ISensor> sensors)
    {
        _lifetime = lifetime;
        _templates = templates;
        _adapters = adapters;
        _scheduler = new SamplingScheduler(sensors, OnSnapshot);
        WatchEnergySaver();
    }

    /// <summary>
    /// Restores widgets that were pinned before this process started. The host keeps them across
    /// provider restarts, together with their CustomState (the widget's settings).
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
                    Settings = WidgetSettings.Parse(info.CustomState),
                });
            }
            Log.Info(added
                ? $"Recovered widget {context.Id} size={context.Size} active={context.IsActive} settings={info.CustomState}"
                : $"Widget {context.Id} already known (host callback arrived first)");

            if (added && context.IsActive)
            {
                SetActive(context.Id, true, "recovered active");
            }
        }

        StateChanged();
    }

    // ----- IWidgetProvider -------------------------------------------------------------------

    public void CreateWidget(WidgetContext widgetContext)
    {
        Log.Info($"CreateWidget {widgetContext.Id} def={widgetContext.DefinitionId} size={widgetContext.Size}");
        var session = new WidgetSession(widgetContext.Id, widgetContext.DefinitionId)
        {
            Size = widgetContext.Size,
        };
        lock (_gate)
        {
            _sessions[session.Id] = session;
        }
        // A newly created widget starts in the active state (IWidgetProvider.Activate docs).
        SetActive(session.Id, true, "created");
        StateChanged();
        Push(session, CurrentSnapshot());
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        Log.Info($"DeleteWidget {widgetId}");
        lock (_gate)
        {
            _sessions.Remove(widgetId);
        }
        StateChanged();
    }

    public void Activate(WidgetContext widgetContext)
    {
        var session = GetOrAdd(widgetContext);
        lock (_gate)
        {
            session.Size = widgetContext.Size;
            session.NeedsTemplate = true;
        }
        SetActive(session.Id, true, $"size={widgetContext.Size}");
        StateChanged();
        Push(session, CurrentSnapshot());
    }

    public void Deactivate(string widgetId)
    {
        SetActive(widgetId, false, null);
        StateChanged();
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        string widgetId = actionInvokedArgs.WidgetContext.Id;
        string verb = actionInvokedArgs.Verb;
        Log.Info($"OnActionInvoked {widgetId} verb={verb}");

        switch (verb)
        {
            case TemplateStore.OpenTaskManagerVerb:
                OpenTaskManager();
                break;

            case SaveSettingsVerb:
            case CancelSettingsVerb:
                WidgetSession? session;
                lock (_gate)
                {
                    if (!_sessions.TryGetValue(widgetId, out session))
                    {
                        return;
                    }
                    if (verb == SaveSettingsVerb)
                    {
                        session.Settings = session.Settings.WithInputs(actionInvokedArgs.Data);
                    }
                    session.InCustomization = false;
                    session.NeedsTemplate = true;
                }
                if (verb == SaveSettingsVerb)
                {
                    Log.Info($"Settings saved for {widgetId}: {session.CustomState}");
                }
                StateChanged();
                Push(session, CurrentSnapshot());
                break;
        }
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        var context = contextChangedArgs.WidgetContext;
        Log.Info($"OnWidgetContextChanged {context.Id} size={context.Size} active={context.IsActive}");
        var session = GetOrAdd(context);
        lock (_gate)
        {
            session.Size = context.Size;
            session.NeedsTemplate = true;
        }
        SetActive(session.Id, context.IsActive, "context changed");
        StateChanged();
        if (context.IsActive)
        {
            Push(session, CurrentSnapshot());
        }
    }

    // ----- IWidgetProvider2 ------------------------------------------------------------------

    /// <summary>The user picked "Customize widget": show the settings card until Save or Cancel.</summary>
    public void OnCustomizationRequested(WidgetCustomizationRequestedArgs customizationRequestedArgs)
    {
        var session = GetOrAdd(customizationRequestedArgs.WidgetContext);
        Log.Info($"OnCustomizationRequested {session.Id}");
        lock (_gate)
        {
            session.InCustomization = true;
            session.NeedsTemplate = true;
        }
        StateChanged();
        Push(session, null);
    }

    // ----- sampling --------------------------------------------------------------------------

    /// <summary>Called by the sampler on its own thread after every tick.</summary>
    private void OnSnapshot(SystemSnapshot snapshot)
    {
        foreach (var session in ActiveSessions())
        {
            Push(session, snapshot);
        }
    }

    private SystemSnapshot? CurrentSnapshot()
    {
        var latest = _scheduler.Latest;
        return latest is not null && DateTimeOffset.UtcNow - latest.Timestamp < StaleAfter ? latest : null;
    }

    /// <summary>Recomputes the sampling plan and the process lifetime after any widget change.</summary>
    private void StateChanged()
    {
        SamplingPlan plan;
        int count;
        lock (_gate)
        {
            count = _sessions.Count;
            plan = SamplingPlan.From(_sessions.Values.Select(DemandOf));
        }
        var previous = _scheduler.Plan;
        _scheduler.Apply(plan);
        if (plan != previous)
        {
            Log.Info(plan.IsActive
                ? $"Sampling: every {plan.Interval.TotalSeconds:0.#}s for {plan.Size} cards, GPU temperature: {plan.TemperatureLuids}" +
                  (_energySaverOn ? " (energy saver on)" : "")
                : "Sampling stopped (no active widget)");
        }
        _lifetime.OnWidgetCountChanged(count);
    }

    private WidgetDemand DemandOf(WidgetSession s)
    {
        var options = OptionsOf(s);
        return new WidgetDemand(
            IsActive: s.IsActive && !s.InCustomization,
            Size: ToCardSize(s.Size),
            Interval: s.Settings.EffectiveInterval(_energySaverOn),
            TemperatureLuid: options.ShowGpuTemperature ? options.GpuLuid ?? _adapters.Default?.Luid : null);
    }

    private CardOptions OptionsOf(WidgetSession s) => new(
        GpuLuid: _adapters.FindByName(s.Settings.GpuName)?.Luid,
        ShowGpuTemperature: s.Settings.EffectiveShowGpuTemperature(_adapters.IsHybrid));

    private void SetActive(string widgetId, bool active, string? reason)
    {
        WidgetSession? session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(widgetId, out session) || session.IsActive == active)
            {
                return;
            }
            session.IsActive = active;
            if (active)
            {
                session.ActiveSince = Stopwatch.GetTimestamp();
                session.UpdatesWhileActive = 0;
            }
        }

        if (active)
        {
            Log.Info($"Activate {widgetId} ({reason})");
        }
        else
        {
            // Shows how long the host keeps a widget active and how many updates it got,
            // e.g. on the lock screen, without logging every tick.
            var elapsed = Stopwatch.GetElapsedTime(session.ActiveSince);
            Log.Info($"Deactivate {widgetId} after {elapsed.TotalSeconds:0.0}s, {session.UpdatesWhileActive} updates sent");
        }
    }

    // ----- energy saver ----------------------------------------------------------------------

    private void WatchEnergySaver()
    {
        try
        {
            _energySaverOn = PowerManager.EnergySaverStatus == EnergySaverStatus.On;
            PowerManager.EnergySaverStatusChanged += (_, _) =>
            {
                _energySaverOn = PowerManager.EnergySaverStatus == EnergySaverStatus.On;
                Log.Info($"Energy saver {(_energySaverOn ? "on" : "off")}");
                StateChanged();
            };
        }
        catch (Exception ex)
        {
            Log.Error("Energy saver status unavailable", ex);
        }
    }

    // ----- helpers ---------------------------------------------------------------------------

    private static void OpenTaskManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Could not open Task Manager", ex);
        }
    }

    private WidgetSession GetOrAdd(WidgetContext context)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(context.Id, out var session))
            {
                // The host can activate a widget this process has not seen (e.g. after a restart
                // that raced with recovery). Track it instead of dropping the call.
                session = new WidgetSession(context.Id, context.DefinitionId) { Size = context.Size };
                _sessions[context.Id] = session;
            }
            return session;
        }
    }

    private List<WidgetSession> ActiveSessions()
    {
        lock (_gate)
        {
            return [.. _sessions.Values.Where(s => s.IsActive && !s.InCustomization)];
        }
    }

    private void Push(WidgetSession session, SystemSnapshot? snapshot)
    {
        string data;
        string template;
        bool sendTemplate;
        lock (_gate)
        {
            if (session.InCustomization)
            {
                data = SettingsCard.BuildData(session.Settings, _adapters.Adapters, _adapters.Default);
                template = _templates.Settings ?? _templates.Composed;
            }
            else
            {
                data = CardData.Build(snapshot, ToCardSize(session.Size), OptionsOf(session));
                template = _templates.Composed;
            }

            sendTemplate = session.NeedsTemplate;
            if (!sendTemplate && data == session.LastData)
            {
                return; // nothing visible changed, spare the host a re-render
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
                options.Template = template;
            }
            WidgetManager.GetDefault().UpdateWidget(options);
            lock (_gate)
            {
                session.UpdatesWhileActive++;
            }
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

    private static CardSize ToCardSize(WidgetSize size) => size switch
    {
        WidgetSize.Medium => CardSize.Medium,
        WidgetSize.Large => CardSize.Large,
        _ => CardSize.Small,
    };

    public ValueTask DisposeAsync() => _scheduler.DisposeAsync();
}
