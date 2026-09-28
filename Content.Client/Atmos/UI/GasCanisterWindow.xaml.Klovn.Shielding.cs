using Robust.Client.UserInterface.CustomControls;

namespace Content.Client.Atmos.UI;

public sealed partial class GasCanisterWindow : DefaultWindow
{
    public event Action? ShieldToggleButtonPressed;

    public void SetShieldSettingsVisible(bool visible)
    {
        ShieldSettingsContainer.Visible = visible;
    }

    public void UpdateShieldState(bool enabled, float dischargeRate, float lastCharge, float maxCharge)
    {
        ShieldToggleButton.Pressed = enabled;
        ShieldToggleButton.Disabled = lastCharge < dischargeRate;
        ShieldWattage.Text = Loc.GetString("gas-canister-shielding-ui-wattage", ("wattage", dischargeRate));
        KsShieldBar.Value = lastCharge / maxCharge;
    }
}
