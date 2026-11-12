using Content.Shared._KS14.Particles;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Particles;

/// <summary>
///     The server's half of <see cref="KsSharedParticlesSystem"/>: it makes entities and sets their fields, and
///         simulates nothing.
/// </summary>
public sealed partial class KsParticlesSystem : KsSharedParticlesSystem
{
    public override EntityUid? SpawnLocal(ProtoId<KsParticlesPrototype> prototype, EntityCoordinates coordinates) => null;

    public override EntityUid? SpawnPvs(ProtoId<KsParticlesPrototype> prototype, EntityCoordinates coordinates)
    {
        var uid = SpawnAttachedTo(ParticlesEntity, coordinates);
        SetPrototype(uid, prototype);
        return uid;
    }
}
