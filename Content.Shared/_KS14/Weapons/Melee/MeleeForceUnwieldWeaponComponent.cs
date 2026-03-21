using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Weapons.Melee;

/// <summary>
/// Component that forces the target to unwield their weapon when hit with a melee attack.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MeleeForceUnwieldWeaponComponent : Component
{
}
