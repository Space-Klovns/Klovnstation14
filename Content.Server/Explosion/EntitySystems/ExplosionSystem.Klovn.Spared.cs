using Content.Shared.Explosion;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    [Dependency] private EntityWhitelistSystem _ksEntityWhitelistSystem = default!;

    /// <summary>
    ///     Whether explosions of type <paramref name="explosionId"/> leave <paramref name="uid"/> undamaged, by
    ///         <see cref="ExplosionPrototype.KsSpared"/> and <see cref="ExplosionPrototype.KsNotSpared"/>.
    /// </summary>
    private bool KsIsSpared(EntityUid uid, ProtoId<ExplosionPrototype> explosionId)
    {
        if (!ProtoMan.TryIndex(explosionId, out var explosionPrototype) ||
            explosionPrototype.KsSpared == null)
            return false;

        return _ksEntityWhitelistSystem.IsWhitelistPass(explosionPrototype.KsSpared, uid) &&
               _ksEntityWhitelistSystem.IsWhitelistFailOrNull(explosionPrototype.KsNotSpared, uid);
    }
}
