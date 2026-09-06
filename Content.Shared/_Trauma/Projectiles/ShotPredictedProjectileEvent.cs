using Robust.Shared.Serialization;

namespace Content.Shared._Trauma.Projectiles;

/// <summary>
/// Event sent to the client that shot a predicted projectile.
/// Used to hide the server-spawned one.
/// </summary>
[Serializable, NetSerializable]
public sealed class ShotPredictedProjectileEvent(NetEntity projectile) : EntityEventArgs
{
    public NetEntity Projectile = projectile;
}
