using Robust.Shared.GameStates;

namespace Content.Shared._KS14.BatteryShielding;

/// <summary>
///     IT'S ABOUT TO BLOW GTFO
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedBatteryShieldingSystem))]
[AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class BtfoingBatteryShieldingComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan BtfoTime;

    [DataField, AutoNetworkedField]
    public EntityUid? UserUid = null;
}
