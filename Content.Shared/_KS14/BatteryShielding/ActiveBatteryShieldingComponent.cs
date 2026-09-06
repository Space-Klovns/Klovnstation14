using Robust.Shared.GameStates;

namespace Content.Shared._KS14.BatteryShielding;

/// <summary>
///     Added to a battery shielding entity if it has a nonzero charge rate
///         or some shit like that idfk.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedBatteryShieldingSystem))]
public sealed partial class ActiveBatteryShieldingComponent : Component;
