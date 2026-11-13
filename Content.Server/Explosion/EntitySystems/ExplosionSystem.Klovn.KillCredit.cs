using Content.Shared.Projectiles;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    /// <summary>
    ///     Whoever set off the explosion whose damage is being dealt right now, or null outside of that.
    /// </summary>
    /// <remarks>
    ///     Explosion damage carries no origin, and giving it one would change everything that reacts to an origin
    ///         (NPC retaliation, kill tracking). This lets code that wants to credit an explosion kill ask instead,
    ///         from inside the handlers its damage raises.
    /// </remarks>
    [ViewVariables]
    public EntityUid? KsProcessingUser { get; private set; }

    /// <summary>
    ///     Who set off <paramref name="explosiveUid"/>. A projectile's trigger names what it hit as the user, so for
    ///         a rocket or shell it's whoever fired it.
    /// </summary>
    private EntityUid? KsResolveUser(EntityUid explosiveUid, EntityUid? user)
    {
        return TryComp<ProjectileComponent>(explosiveUid, out var projectileComponent) && projectileComponent.Shooter is { } shooterUid
            ? shooterUid
            : user;
    }
}
