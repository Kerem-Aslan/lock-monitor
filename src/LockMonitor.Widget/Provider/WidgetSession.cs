using LockMonitor.Rendering;
using Microsoft.Windows.Widgets;

namespace LockMonitor.Provider;

/// <summary>State the provider keeps for one pinned widget instance (Widgets Board or lock screen).</summary>
internal sealed class WidgetSession(string id, string definitionId)
{
    public string Id { get; } = id;
    public string DefinitionId { get; } = definitionId;
    public WidgetSize Size { get; set; } = WidgetSize.Small;
    public bool IsActive { get; set; }

    /// <summary>User settings; persisted as <see cref="CustomState"/>.</summary>
    public WidgetSettings Settings { get; set; } = WidgetSettings.Default;

    /// <summary>Per-widget settings as the widget host stores them (JSON).</summary>
    public string CustomState => Settings.ToJson();

    /// <summary>Where the "Customize widget" flow is; see <see cref="CustomizationState"/>.</summary>
    public CustomizationState Customization { get; set; }

    /// <summary>The settings card is showing (or about to); live updates are paused for this widget.</summary>
    public bool InCustomization => Customization != CustomizationState.None;

    /// <summary>Last data payload sent to the host, used to skip identical updates.</summary>
    public string? LastData { get; set; }

    /// <summary>True when the host must receive the template again (create, activate, resize, settings).</summary>
    public bool NeedsTemplate { get; set; } = true;

    // Diagnostics for the lifecycle log
    public long ActiveSince { get; set; }
    public int UpdatesWhileActive { get; set; }

    public override string ToString() => $"{Id} ({DefinitionId}, {Size}, {(IsActive ? "active" : "inactive")})";
}

/// <summary>
/// The host's customization flow, as observed in the provider log (Windows 11 26H2):
/// OnCustomizationRequested, possibly a Deactivate of the board card (order varies), then an
/// Activate for the settings flyout. Closing the flyout with its X sends no action, only a
/// Deactivate followed by an Activate of the widget itself.
/// </summary>
internal enum CustomizationState
{
    None,

    /// <summary>Requested; a Deactivate now only hides the board card and is ignored.</summary>
    Requested,

    /// <summary>The flyout was activated; a Deactivate now means it was closed.</summary>
    Showing,
}
