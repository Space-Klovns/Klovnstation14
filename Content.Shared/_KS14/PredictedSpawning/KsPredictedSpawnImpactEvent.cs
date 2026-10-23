using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.PredictedSpawning;

/// <summary>
///     Sent to a user-mode spawn's user when the server's copy of their projectile hits something. Their client's
///         kept copy plays its own hits at once, but it is only a guess at where the shot goes: this plays the real
///         hit's impact there too, unless the kept copy already played one on the same target. Everyone else predicts
///         the server's copy hitting, as for any projectile.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsPredictedSpawnImpactEvent : EntityEventArgs
{
    /// <summary>
    ///     The server's copy of the projectile.
    /// </summary>
    public NetEntity Projectile;

    public NetEntity Target;

    /// <summary>
    ///     Where the server's copy was when it hit.
    /// </summary>
    public NetCoordinates Coordinates;

    public EntProtoId? ImpactEffect;

    public SoundSpecifier? SoundHit;

    public bool ForceSound;

    /// <summary>
    ///     The damage the hit dealt, which picks the target's impact sound.
    /// </summary>
    public DamageSpecifier? Damage;
}
