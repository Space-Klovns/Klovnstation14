using Content.Shared._KS14.Particles;
using Content.Shared.FixedPoint;

namespace Content.Shared._KS14.Damage.DamagedParticles;

/// <summary>
///     Turns this entity's <see cref="KsParticlesComponent"/> on while its total damage is at or above
///         <see cref="Threshold"/>, and off once it is repaired below it: a damaged machine's smoke.
/// </summary>
/// <remarks>
///     Not networked: the threshold comes from the prototype, and the emitter's own state is what is networked.
/// </remarks>
[RegisterComponent]
[Access(typeof(KsDamagedParticlesSystem))]
public sealed partial class KsDamagedParticlesComponent : Component
{
    [DataField(required: true)]
    public FixedPoint2 Threshold;
}
