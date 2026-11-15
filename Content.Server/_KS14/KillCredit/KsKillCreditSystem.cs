using Content.Server.Explosion.EntitySystems;
using Content.Shared._KS14.Construction;
using Content.Shared._Trauma.Projectiles;
using Content.Shared.Construction.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._KS14.KillCredit;

/// <summary>
///     Answers two questions about damage, which are not the same thing:
///     <list type="bullet">
///         <item>
///             <b>What did it</b> (<see cref="GetAttacker"/>): the thing itself, a turret rather than its owner. For
///                 whatever reacts to being hurt, such as an NPC turning to fire back.
///         </item>
///         <item>
///             <b>Who is paid for it</b> (<see cref="GetHitRecipient"/>): a player, asked of the attacker with
///                 <see cref="KsGetCreditRecipientEvent"/> as the hit lands. A player's body answers with its player,
///                 a turret with whoever set it up, a signaller with whoever pressed it, and a held gun with
///                 whoever holds it.
///         </item>
///     </list>
///     Whatever can outlive what it answers for is told the answer early: a bullet is stamped with its shooter's
///         recipient as it is fired, and an explosion is given one as it is queued.
/// </summary>
public sealed partial class KsKillCreditSystem : EntitySystem
{
    /// <summary>
    ///     How many entities may pass the question on (a bullet to its shooter...) before giving up, so a loop of
    ///         them can't hang the server.
    /// </summary>
    private const int MaxDepth = 8;

    [Dependency] private ExplosionSystem _explosionSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;

    /// <summary>
    ///     What really dealt damage with this <paramref name="origin"/>: the shooter of a bullet, and for damage with
    ///         no origin, inside an explosion, whatever set it off. Never an owner. It may be deleted already.
    /// </summary>
    public EntityUid? GetAttacker(EntityUid? origin)
    {
        var attackerUid = origin ?? _explosionSystem.KsProcessingUser;

        if (attackerUid is { } uid &&
            _projectileQuery.TryComp(uid, out var projectileComponent) &&
            projectileComponent.Shooter is { } shooterUid)
            return shooterUid;

        return attackerUid;
    }

    /// <summary>
    ///     The player who is paid for damage dealt with this <paramref name="origin"/>, or null if nobody is. To be
    ///         asked as the damage lands, while the origin still exists.
    /// </summary>
    public NetUserId? GetHitRecipient(EntityUid? origin)
    {
        return origin is { } originUid ? GetRecipient(originUid) : _explosionSystem.KsProcessingRecipient;
    }

    /// <summary>
    ///     The player who is paid for what <paramref name="uid"/> does, or null if nobody is, or it is gone.
    /// </summary>
    public NetUserId? GetRecipient(EntityUid uid)
    {
        return GetRecipient(uid, depth: 0);
    }

    private NetUserId? GetRecipient(EntityUid uid, int depth)
    {
        if (depth >= MaxDepth || Deleted(uid))
            return null;

        var ev = new KsGetCreditRecipientEvent(Depth: depth);
        RaiseLocalEvent(uid, ref ev);
        return ev.Recipient;
    }

    /// <summary>
    ///     A player's own body is paid for what it does.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnActorGetRecipient(Entity<ActorComponent> entity, ref KsGetCreditRecipientEvent args)
    {
        args.Recipient ??= entity.Comp.PlayerSession.UserId;
    }

    [SubscribeLocalEvent]
    private void OnOwnedGetRecipient(Entity<KsCreditOwnerComponent> entity, ref KsGetCreditRecipientEvent args)
    {
        args.Recipient ??= entity.Comp.OwnerUserId;
    }

    /// <summary>
    ///     A gun in someone's hands, or anywhere else on them, answers for whoever holds it: a beam names the gun as
    ///         what did the damage, not the shooter. A gun that is its own thing, a turret or a mob that shoots, is
    ///         in nothing, and answers through whatever else is on it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnGunGetRecipient(Entity<GunComponent> entity, ref KsGetCreditRecipientEvent args)
    {
        if (args.Recipient == null && _containerSystem.TryGetContainingContainer(entity.Owner, out var container))
            args.Recipient = GetRecipient(container.Owner, args.Depth + 1);
    }

    /// <summary>
    ///     A bullet that was never stamped answers for its shooter, while that still exists.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnProjectileGetRecipient(Entity<ProjectileComponent> entity, ref KsGetCreditRecipientEvent args)
    {
        if (args.Recipient == null && entity.Comp.Shooter is { } shooterUid && shooterUid != entity.Owner)
            args.Recipient = GetRecipient(shooterUid, args.Depth + 1);
    }

    /// <summary>
    ///     A device whose signal set something off answers for whoever sent the signal by using it
    ///         (<see cref="DeviceLinkSourceComponent.KsSignalUser"/>): the trigger names the device, not the presser.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSignalSourceGetRecipient(Entity<DeviceLinkSourceComponent> entity, ref KsGetCreditRecipientEvent args)
    {
        if (args.Recipient == null && entity.Comp.KsSignalUser is { } signalUserUid && signalUserUid != entity.Owner)
            args.Recipient = GetRecipient(signalUserUid, args.Depth + 1);
    }

    /// <summary>
    ///     A bullet is stamped with whoever its shooter answers to, which outlives the shooter: a disposable turret
    ///         blows up on its last shot, before the bullet lands.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnPlayerShotProjectile(ref PlayerShotProjectileEvent args)
    {
        if (GetRecipient(args.User) is { } recipient)
            SetOwner(args.Projectile, recipient);
    }

    /// <summary>
    ///     What comes out of a flatpack answers to whoever unpacked it.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnFlatpackUnpacked(Entity<FlatpackComponent> entity, ref KsFlatpackUnpackedEvent args)
    {
        if (GetRecipient(args.User) is { } recipient)
            SetOwner(args.Unpacked, recipient);
    }

    /// <summary>
    ///     Makes <paramref name="uid"/> answer to a player from here on.
    /// </summary>
    public void SetOwner(EntityUid uid, NetUserId ownerUserId)
    {
        EnsureComp<KsCreditOwnerComponent>(uid).OwnerUserId = ownerUserId;
    }
}
