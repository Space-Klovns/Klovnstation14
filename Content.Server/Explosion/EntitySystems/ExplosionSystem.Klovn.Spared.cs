using Content.Shared.Explosion;
using Content.Shared.Whitelist;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    [Dependency] private EntityWhitelistSystem _ksEntityWhitelistSystem = default!;

    /// <summary>
    ///     Whether explosions of type <paramref name="explosionId"/> leave <paramref name="uid"/> undamaged, by
    ///         <see cref="ExplosionPrototype.KsSpared"/> and <see cref="ExplosionPrototype.KsNotSpared"/>.
    /// </summary>
    private bool KsIsSpared(EntityUid uid, string explosionId)
    {
        if (!ProtoMan.TryIndex<ExplosionPrototype>(explosionId, out var explosionPrototype) ||
            explosionPrototype.KsSpared == null)
            return false;

        return _ksEntityWhitelistSystem.IsWhitelistPass(explosionPrototype.KsSpared, uid) &&
               _ksEntityWhitelistSystem.IsWhitelistFailOrNull(explosionPrototype.KsNotSpared, uid);
    }
}
