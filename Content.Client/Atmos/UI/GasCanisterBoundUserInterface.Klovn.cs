using Content.Client.Power.EntitySystems;
using Content.Shared._KS14.BatteryShielding;
using Content.Shared.Power.Components;

namespace Content.Client.Atmos.UI;

public sealed partial class GasCanisterBoundUserInterface : BoundUserInterface
{
    private readonly BatterySystem _batterySystem = default!;

    private void OnToggleShieldingPressed()
    {
        SendPredictedMessage(new BatteryShieldingToggleMessage());
    }

    public override void Update()
    {
        base.Update();

        if (_window != null)
        {
            if (EntMan.TryGetComponent(Owner, out BatteryShieldingComponent? shieldingComponent) &&
                EntMan.TryGetComponent(Owner, out BatteryComponent? batteryComponent))
            {
                _window.SetShieldSettingsVisible(true);

                // GetCharge is used rather than LastCharge so that self-recharging can be accounted for
                _window.UpdateShieldState(shieldingComponent.Enabled, shieldingComponent.DischargeRate, _batterySystem.GetCharge((Owner, batteryComponent)), batteryComponent.MaxCharge);
            }
            else
                _window.SetShieldSettingsVisible(false);
        }
    }
}
