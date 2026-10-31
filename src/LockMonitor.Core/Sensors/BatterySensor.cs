using LockMonitor.Rendering;
using LockMonitor.Sampling;
using Windows.System.Power;

namespace LockMonitor.Sensors;

/// <summary>
/// Battery level and charge state from Windows.System.Power.PowerManager (no capability or admin
/// needed). Values are refreshed by PowerManager's change events while the sensor is open, so
/// sampling only copies the cached reading; nothing is polled.
/// </summary>
public sealed class BatterySensor : ISensor
{
    private volatile BatteryReading? _reading;
    private bool _subscribed;

    public string Name => "Battery";
    public CardSize RequiredSize => CardSize.Large;
    public TimeSpan RefreshInterval => TimeSpan.Zero;

    public void Open()
    {
        Refresh();
        PowerManager.RemainingChargePercentChanged += OnChanged;
        PowerManager.BatteryStatusChanged += OnChanged;
        PowerManager.PowerSupplyStatusChanged += OnChanged;
        PowerManager.RemainingDischargeTimeChanged += OnChanged;
        _subscribed = true;
    }

    public void Sample(SystemSnapshot snapshot, in SamplingPlan plan) => snapshot.Battery = _reading;

    public void Clear(SystemSnapshot snapshot) => snapshot.Battery = null;

    public void Close()
    {
        if (!_subscribed)
        {
            return;
        }
        PowerManager.RemainingChargePercentChanged -= OnChanged;
        PowerManager.BatteryStatusChanged -= OnChanged;
        PowerManager.PowerSupplyStatusChanged -= OnChanged;
        PowerManager.RemainingDischargeTimeChanged -= OnChanged;
        _subscribed = false;
    }

    private void OnChanged(object? sender, object e) => Refresh();

    private void Refresh()
    {
        try
        {
            _reading = Read(PowerManager.BatteryStatus, PowerManager.PowerSupplyStatus,
                PowerManager.RemainingChargePercent, PowerManager.RemainingDischargeTime);
        }
        catch (Exception ex)
        {
            Log.Error("Reading the battery state failed", ex);
            _reading = null;
        }
    }

    /// <summary>Maps PowerManager values to a reading; null when there is no battery.</summary>
    internal static BatteryReading? Read(BatteryStatus battery, PowerSupplyStatus supply, int percent, TimeSpan remaining)
    {
        if (battery == BatteryStatus.NotPresent)
        {
            return null;
        }
        var state = battery switch
        {
            BatteryStatus.Charging => ChargeState.Charging,
            BatteryStatus.Discharging when supply == PowerSupplyStatus.NotPresent => ChargeState.Discharging,
            BatteryStatus.Discharging => ChargeState.Charging, // inadequate charger: still on AC
            _ => supply == PowerSupplyStatus.NotPresent ? ChargeState.Discharging : ChargeState.PluggedIn,
        };
        // RemainingDischargeTime is TimeSpan.MaxValue (or 0) when Windows has no estimate.
        TimeSpan? left = state == ChargeState.Discharging && remaining > TimeSpan.Zero && remaining < TimeSpan.FromDays(2)
            ? remaining : null;
        return new BatteryReading(Math.Clamp(percent, 0, 100), state, left);
    }
}
