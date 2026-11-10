using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._KS14.PredictedSpawning;

/// <summary>
///     The server's copy of a projectile hit something. Only the client that shot it predicts its flight, and so its
///         hit; every other client interpolates it, and plays the hit's impact from this.
///
///     Sent separately to a user-mode spawn's user (<see cref="ToPredictingUser"/>): their client's kept copy plays its
///         own hits at once, but it is only a guess at where the shot goes, so this plays the real hit's impact there
///         too, unless the kept copy already played this shot's.
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
    ///     Whether this is sent to the user whose client keeps its own copy of the projectile, rather than to a client
    ///         that only saw the server's.
    /// </summary>
    public bool ToPredictingUser;

    /// <summary>
    ///     The projectile's prototype and <see cref="KsPredictedSpawnComponent.SpawnTick"/>, sent to the predicting
    ///         user so their client can tell which of its kept copies this shot is even when the server's copy never
    ///         reached it (one spawned and spent within a tick, point blank).
    /// </summary>
    public EntProtoId? Prototype;

    /// <inheritdoc cref="Prototype"/>
    public GameTick SpawnTick;

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
