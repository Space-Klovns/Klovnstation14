using Content.Shared._Trauma.Projectiles;
using Content.Shared.NPC.Components;
using Content.Shared.Projectiles;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;

namespace Content.Shared._KS14.FactionCollision;

/// <summary>
///     The shots of a <see cref="KsFactionCollisionShooterComponent"/> pass through its own side: whoever shares an NPC
///         faction with it.
/// </summary>
/// <remarks>
///     The server goes by the factions. A client can't: <see cref="NpcFactionMemberComponent.Factions"/> is not sent to
///         it, and what it has is the factions of each entity's prototype, which for a humanoid are all the same. Judged
///         by those, a player's own predicted shot went through everyone the server's then hit. So each player is sent,
///         for every faction member, whether their own shots pass through it
///         (<see cref="NpcFactionMemberComponent.KsLocalPlayerShotsPass"/>), and their client's shots go by that.
/// </remarks>
public sealed partial class KsFactionCollisionSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ISharedPlayerManager _playerManager = default!;
    [Dependency] private Content.Shared.NPC.Systems.NpcFactionSystem _npcFactionSystem = default!;

    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;
    [Dependency] private EntityQuery<KsFactionCollisionShooterComponent> _factionCollisionShooterQuery = default!;
    [Dependency] private EntityQuery<ProjectileComponent> _projectileQuery = default!;
    [Dependency] private EntityQuery<ActorComponent> _actorQuery = default!;

    /// <summary>
    ///     Whether what every faction member's state says is out of date for some player, whose side or body has
    ///         changed: they are all sent again, once, on the next update.
    /// </summary>
    private bool _everyStateStale;

    // its almost like linq except not actually under System.Linq

    [SubscribeLocalEvent(after: [typeof(Content.Shared.Projectiles.SharedProjectileSystem)])]
    private void OnPreventCollide(Entity<KsFactionCollisionComponent> entity, ref PreventCollideEvent args)
    {
        if (args.Cancelled ||
            !_factionMemberQuery.TryGetComponent(args.OtherEntity, out var otherFactionComponent))
            return;

        if (_netManager.IsClient)
        {
            // Only its own player's shots collide with anything here, and the server has said which they pass through.
            if (!_projectileQuery.TryComp(entity.Owner, out var projectileComponent) ||
                projectileComponent.Shooter is not { } shooterUid ||
                shooterUid != _playerManager.LocalEntity ||
                !otherFactionComponent.KsLocalPlayerShotsPass)
                return;
        }
        else if (!entity.Comp.Factions.Overlaps(otherFactionComponent.Factions))
        {
            return;
        }

        args.Cancelled = true;
    }

    [SubscribeLocalEvent]
    private void OnPlayerShotProjectile(ref PlayerShotProjectileEvent args)
    {
        if (!_factionCollisionShooterQuery.HasComponent(args.User))
            return;

        // A client's copy of its player's shot has no factions worth having: it goes by what the server said of each
        //      thing in its way (OnPreventCollide).
        if (_netManager.IsClient)
        {
            EnsureComp<KsFactionCollisionComponent>(args.Projectile);
            return;
        }

        if (!_factionMemberQuery.TryGetComponent(args.User, out var factionMemberComponent) ||
            factionMemberComponent.Factions.Count == 0)
            return;

        var collisionComponent = EnsureComp<KsFactionCollisionComponent>(args.Projectile);
        collisionComponent.Factions.UnionWith(factionMemberComponent.Factions);

        Dirty(args.Projectile, collisionComponent);
    }

    /// <summary>
    ///     Whether the server has <paramref name="shooterUid"/>'s shots pass through <paramref name="target"/>.
    /// </summary>
    private bool ShotsPass(EntityUid shooterUid, Entity<NpcFactionMemberComponent> target)
    {
        return shooterUid != target.Owner &&
               _factionCollisionShooterQuery.HasComponent(shooterUid) &&
               _factionMemberQuery.TryGetComponent(shooterUid, out var shooterFactionComponent) &&
               _npcFactionSystem.IsMemberOfAny(target.AsNullable(), shooterFactionComponent.Factions);
    }

    [SubscribeLocalEvent]
    private void OnGetState(Entity<NpcFactionMemberComponent> entity, ref ComponentGetState args)
    {
        var shotsPass = args.Player?.AttachedEntity is { } playerUid && ShotsPass(playerUid, entity);
        args.State = new KsNpcFactionMemberComponentState(shotsPass);
    }

    [SubscribeLocalEvent]
    private void OnHandleState(Entity<NpcFactionMemberComponent> entity, ref ComponentHandleState args)
    {
        if (args.Current is KsNpcFactionMemberComponentState state)
            entity.Comp.KsLocalPlayerShotsPass = state.ShotsPass;
    }

    [SubscribeLocalEvent]
    private void OnFactionsRefreshed(Entity<NpcFactionMemberComponent> entity, ref KsNpcFactionsRefreshedEvent args)
    {
        if (_netManager.IsClient)
            return;

        // What every player was told of it.
        Dirty(entity);

        // And what its own player was told of everyone else.
        MarkStaleIfPlayersShooter(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnShooterStartup(Entity<KsFactionCollisionShooterComponent> entity, ref ComponentStartup args)
    {
        MarkStaleIfPlayersShooter(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnShooterShutdown(Entity<KsFactionCollisionShooterComponent> entity, ref ComponentShutdown args)
    {
        if (!_netManager.IsClient && _actorQuery.HasComponent(entity.Owner))
            _everyStateStale = true;
    }

    // A by-value event, so no Entity<T> handler (RA0054).
    [SubscribeLocalEvent]
    private void OnShooterPlayerAttached(EntityUid uid, KsFactionCollisionShooterComponent component, PlayerAttachedEvent args)
    {
        if (!_netManager.IsClient)
            _everyStateStale = true;
    }

    /// <summary>
    ///     What <paramref name="uid"/>'s player was told of every faction member is out of date, if it has a player
    ///         whose shots pass through anyone.
    /// </summary>
    private void MarkStaleIfPlayersShooter(EntityUid uid)
    {
        if (!_netManager.IsClient &&
            _factionCollisionShooterQuery.HasComponent(uid) &&
            _actorQuery.HasComponent(uid))
            _everyStateStale = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_everyStateStale)
            return;

        _everyStateStale = false;

        // Paused ones too: a state says the same of them.
        var query = AllEntityQuery<NpcFactionMemberComponent>();
        while (query.MoveNext(out var uid, out var factionMemberComponent))
        {
            Dirty(uid, factionMemberComponent);
        }
    }
}
