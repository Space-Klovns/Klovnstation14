using Content.Server._KS14.KillCredit;
using Content.Shared.Projectiles;
using Robust.Shared.Network;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    [Dependency] private KsKillCreditSystem _ksKillCreditSystem = default!;

    /// <summary>
    ///     Whatever set off the explosion whose damage is being dealt right now, or null outside of that. The thing
    ///         itself: a turret that blew itself up, not its owner. It may be deleted by now.
    /// </summary>
    /// <remarks>
    ///     Explosion damage carries no origin, and giving it one would change everything that reacts to an origin
    ///         (NPC retaliation, kill tracking). This lets code that wants to know what was behind an explosion ask
    ///         instead, from inside the handlers its damage raises.
    /// </remarks>
    [ViewVariables]
    public EntityUid? KsProcessingUser { get; private set; }

    /// <summary>
    ///     The player who is paid for what the explosion being dealt right now kills. Null if nobody is, and outside
    ///         of that.
    /// </summary>
    [ViewVariables]
    public NetUserId? KsProcessingRecipient { get; private set; }

    /// <summary>
    ///     What set off <paramref name="explosiveUid"/>, and who is paid for it. A projectile's trigger names what it
    ///         hit as the user, so for a rocket or shell it's whatever fired it.
    /// </summary>
    /// <param name="recipient">
    ///     Settled now, while a turret that blows itself up still exists to say whose it is: the explosion is dealt
    ///         over the next few ticks, by which time it has been deleted.
    /// </param>
    private EntityUid? KsResolveUser(EntityUid explosiveUid, EntityUid? user, out NetUserId? recipient)
    {
        if (TryComp<ProjectileComponent>(explosiveUid, out var projectileComponent) && projectileComponent.Shooter is { } shooterUid)
        {
            // The shell answers for its shooter, or for whoever it was stamped with if the shooter is gone.
            recipient = _ksKillCreditSystem.GetRecipient(explosiveUid);
            return shooterUid;
        }

        // Whoever set it off, or failing that whoever the explosive itself answers to: a turret that blows itself up.
        recipient = (user is { } userUid ? _ksKillCreditSystem.GetRecipient(userUid) : null)
            ?? _ksKillCreditSystem.GetRecipient(explosiveUid);
        return user;
    }
}
