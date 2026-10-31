using LockMonitor.Sampling;
using Microsoft.Windows.Widgets;

namespace LockMonitor.Provider;

/// <summary>State the provider keeps for one pinned widget instance (Widgets Board or lock screen).</summary>
internal sealed class WidgetSession(string id, string definitionId)
{
    public string Id { get; } = id;
    public string DefinitionId { get; } = definitionId;
    public WidgetSize Size { get; set; } = WidgetSize.Small;
    public bool IsActive { get; set; }

    /// <summary>Requested update interval. Phase 3 reads it from CustomState (user setting).</summary>
    public TimeSpan Interval { get; set; } = SamplingPlan.DefaultInterval;

    /// <summary>Per-widget settings, persisted by the widget host as CustomState (JSON).</summary>
    public string CustomState { get; set; } = string.Empty;

    /// <summary>Last data payload sent to the host, used to skip identical updates.</summary>
    public string? LastData { get; set; }

    /// <summary>True when the host must receive the template again (create, activate, resize).</summary>
    public bool NeedsTemplate { get; set; } = true;

    // Diagnostics for the lifecycle log
    public long ActiveSince { get; set; }
    public int UpdatesWhileActive { get; set; }

    public override string ToString() => $"{Id} ({DefinitionId}, {Size}, {(IsActive ? "active" : "inactive")})";
}
