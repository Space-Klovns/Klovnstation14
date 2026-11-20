using Robust.Shared.GameStates;

namespace Content.Shared._KS14.GunDamageMultiplier;

/// <summary>
///     Scales the damage of every projectile this gun fires. tgstation's <c>projectile_damage_multiplier</c>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsGunDamageMultiplierComponent : Component
{
    [DataField]
    public float Multiplier = 1f;
}
