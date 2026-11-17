using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Chemistry.Explosion;

/// <summary>
///     A portable container that explodes when shot or burned, as strongly as what it holds
///         (<see cref="KsSolutionExplosionSystem"/>). One holding nothing explosive just takes the damage.
/// </summary>
[RegisterComponent, Access(typeof(KsExplosiveContainerSystem))]
public sealed partial class KsExplosiveContainerComponent : Component
{
    [DataField(required: true)]
    public string Solution = default!;

    /// <summary>
    ///     How much damage of any of <see cref="DamageTypes"/> sets it off, counted together.
    /// </summary>
    [DataField]
    public float DamageThreshold = 5f;

    [DataField]
    public List<ProtoId<DamageTypePrototype>> DamageTypes = new() { "Heat", "Piercing" };
}
