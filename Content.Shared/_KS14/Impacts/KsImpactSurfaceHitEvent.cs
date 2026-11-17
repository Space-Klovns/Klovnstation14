using System.Numerics;
using Content.Shared.Damage;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Impacts;

/// <summary>
///     A hit whose impact the server shows to clients that did not predict it (a hitscan shot by someone else, or by
///         an NPC). Bullets have their own relay, <c>KsPredictedSpawnImpactEvent</c>.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsImpactSurfaceHitEvent : EntityEventArgs
{
    public NetEntity Target;

    /// <summary>
    ///     Where the shot met the target, on its near side.
    /// </summary>
    public NetCoordinates Coordinates;

    /// <summary>
    ///     Which way the shot flew, in world space.
    /// </summary>
    public Vector2 Direction;

    public DamageSpecifier? RawDamage;

    public DamageSpecifier? Damage;
}
