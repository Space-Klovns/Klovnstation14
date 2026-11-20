using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Perks.Support;

/// <summary>
///     A kill the holder lands (or their companion lands for them) has a <see cref="Chance"/> of dropping a
///         <see cref="Pickup"/> where the victim fell: an ammo resupply any friendly can take
///         (<see cref="KsAmmoPickupComponent"/>). Rolled and spawned on the server (<c>KsAmmoDropSystem</c>).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsAmmoDropAbilityComponent : Component
{
    /// <summary>
    ///     Chance per kill, 0 to 1.
    /// </summary>
    [DataField]
    public float Chance = 0.25f;

    [DataField(required: true)]
    public EntProtoId<KsAmmoPickupComponent> Pickup;
}
