using Content.Shared._KS14.Particles;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Weapons.GunSmoke;

/// <summary>
///     Puffs smoke out of the muzzle along each shot: TGMC's <c>GUN_SMOKE_PARTICLES</c>. Predicted for the shooter.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsGunFiringSmokeComponent : Component
{
    [DataField]
    public ProtoId<KsParticlesPrototype> Prototype = "KsGunFiringSmoke";
}
