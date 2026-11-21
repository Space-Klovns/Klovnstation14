using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._KS14.Dissolve;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.Companions;
using Content.Shared._KS14.FactionCollision;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.Scenario.Components;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Companions;

/// <summary>
///     Spawns and keeps an owner's companions (<see cref="KsCompanionsComponent"/>): each materialises beside the
///         owner (or hovering by it, <see cref="KsCompanionMode.Hover"/>), takes the owner's NPC factions (never a scenario one), and
///         its shots pass through the owner's side. One destroyed comes back after its entry's delay, with a popup to
///         the owner; all of them dissolve when the owner dies, or its player leaves it, and come back when it is
///         revived with its player in it, or the player returns. A companion let go always ends up gone.
/// </summary>
public sealed partial class KsCompanionSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedMindSystem _mindSystem = default!;
    [Dependency] private KsDissolveSystem _dissolveSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NPCSystem _npcSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private SharedPhysicsSystem _physicsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<KsCompanionComponent> _companionQuery = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private EntityQuery<KsCompanionsComponent> _companionsQuery = default!;
    [Dependency] private EntityQuery<HTNComponent> _htnQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;
    [Dependency] private EntityQuery<ScenarioFactionMemberComponent> _scenarioMemberQuery = default!;

    /// <summary>
    ///     A hovering companion this close to its hover point stops there, rather than creeping at a vanishing speed.
    /// </summary>
    private const float HoverSettleDistance = 0.01f;

    public override void Initialize()
    {
        base.Initialize();

        // Hovering companions are steered by setting their velocity before physics steps, so a push it took since
        //     (a shove, an explosion, its own gun's recoil) is overwritten before it ever moves them.
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    #region API

    /// <summary>
    ///     The companion out for an entry of an owner's list, if there is one.
    /// </summary>
    public bool TryGetCompanion(Entity<KsCompanionsComponent?> owner, int index, [NotNullWhen(true)] out EntityUid? companionUid)
    {
        companionUid = null;
        if (!_companionsQuery.Resolve(owner.Owner, ref owner.Comp, logMissing: false) ||
            index < 0 ||
            index >= owner.Comp.Spawned.Count)
            return false;

        companionUid = owner.Comp.Spawned[index];
        return companionUid != null;
    }

    /// <summary>
    ///     Whose companion this is.
    /// </summary>
    public bool TryGetOwner(EntityUid companionUid, [NotNullWhen(true)] out EntityUid? ownerUid)
    {
        ownerUid = _companionQuery.CompOrNull(companionUid)?.OwnerUid;
        return ownerUid != null;
    }

    /// <summary>
    ///     Sends a following companion after something other than its owner for now (a patient), or back to its owner
    ///         with null. Does nothing to a hovering one.
    /// </summary>
    public void SetFollowOverride(Entity<KsCompanionComponent?> companion, EntityUid? targetUid)
    {
        if (!_companionQuery.Resolve(companion.Owner, ref companion.Comp, logMissing: false) ||
            companion.Comp.Mode != KsCompanionMode.Follow ||
            companion.Comp.FollowOverride == targetUid)
            return;

        companion.Comp.FollowOverride = targetUid;
        UpdateFollowTarget((companion.Owner, companion.Comp));
    }

    #endregion

    #region Owner

    [SubscribeLocalEvent]
    private void OnOwnerMapInit(Entity<KsCompanionsComponent> owner, ref MapInitEvent args)
    {
        if (!_mobStateSystem.IsDead(owner.Owner))
            SpawnMissing(owner, returned: false);
    }

    [SubscribeLocalEvent]
    private void OnOwnerShutdown(Entity<KsCompanionsComponent> owner, ref ComponentShutdown args)
    {
        // Taken away with whatever gave them: deleted, not dissolved, as nothing should act for the owner afterwards.
        for (var i = 0; i < owner.Comp.Spawned.Count; i++)
        {
            if (owner.Comp.Spawned[i] is not { } companionUid)
                continue;

            owner.Comp.Spawned[i] = null;
            if (_companionQuery.TryComp(companionUid, out var companionComponent))
                companionComponent.Released = true;

            QueueDel(companionUid);
        }

        owner.Comp.RespawnTimes.Clear();
    }

    // A by-value event, so no Entity<T> handler (RA0054).
    [SubscribeLocalEvent]
    private void OnOwnerMobStateChanged(EntityUid uid, KsCompanionsComponent component, MobStateChangedEvent args)
    {
        var owner = new Entity<KsCompanionsComponent>(uid, component);

        if (args.NewMobState == MobState.Dead)
        {
            // They go with the owner, and wait for it rather than for a timer.
            component.RespawnTimes.Clear();
            for (var i = 0; i < component.Spawned.Count; i++)
                Release(owner, i);

            return;
        }

        // Back only for a body someone is playing: one its player left for another stays without them.
        if (args.OldMobState == MobState.Dead)
        {
            component.RespawnTimes.Clear();
            if (_mindSystem.TryGetMind(uid, out _, out _))
                SpawnMissing(owner, returned: false);
        }
    }

    // The owner's player left it for another body: its companions go with them, as on death. By-value events, so no
    //     Entity<T> handlers (RA0054).
    [SubscribeLocalEvent]
    private void OnOwnerMindRemoved(EntityUid uid, KsCompanionsComponent component, MindRemovedMessage args)
    {
        // A body being deleted takes them with it (OnOwnerShutdown).
        if (TerminatingOrDeleted(uid))
            return;

        component.RespawnTimes.Clear();
        for (var i = 0; i < component.Spawned.Count; i++)
            Release((uid, component), i);
    }

    [SubscribeLocalEvent]
    private void OnOwnerMindAdded(EntityUid uid, KsCompanionsComponent component, MindAddedMessage args)
    {
        if (!_mobStateSystem.IsDead(uid))
            SpawnMissing((uid, component), returned: false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateHovering(frameTime);

        var curTime = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<KsCompanionsComponent>();
        while (query.MoveNext(out var uid, out var companionsComponent))
        {
            if (companionsComponent.RespawnTimes.Count == 0 || _mobStateSystem.IsDead(uid))
                continue;

            for (var i = 0; i < companionsComponent.Companions.Count; i++)
            {
                if (!companionsComponent.RespawnTimes.TryGetValue(i, out var respawnTime) || respawnTime > curTime)
                    continue;

                companionsComponent.RespawnTimes.Remove(i);
                SpawnCompanion((uid, companionsComponent), i, returned: true);
            }
        }
    }

    private void SpawnMissing(Entity<KsCompanionsComponent> owner, bool returned)
    {
        for (var i = 0; i < owner.Comp.Companions.Count; i++)
        {
            if (!TryGetCompanion(owner.AsNullable(), i, out _) && !owner.Comp.RespawnTimes.ContainsKey(i))
                SpawnCompanion(owner, i, returned);
        }
    }

    private void SpawnCompanion(Entity<KsCompanionsComponent> owner, int index, bool returned)
    {
        if (TerminatingOrDeleted(owner.Owner))
            return;

        while (owner.Comp.Spawned.Count <= index)
            owner.Comp.Spawned.Add(null);

        if (owner.Comp.Spawned[index] != null)
            return;

        var entry = owner.Comp.Companions[index];
        // Beside the owner on its grid or map (out of a locker it is in), never on the owner itself.
        var coordinates = _transformSystem.GetMoverCoordinates(owner.Owner);
        if (entry.Mode == KsCompanionMode.Hover)
            coordinates = coordinates.Offset(entry.Offset);

        var companionUid = Spawn(entry.Prototype, coordinates);
        owner.Comp.Spawned[index] = companionUid;

        var companionComponent = EnsureComp<KsCompanionComponent>(companionUid);
        companionComponent.OwnerUid = owner.Owner;
        companionComponent.Index = index;
        companionComponent.Mode = entry.Mode;
        companionComponent.Offset = entry.Offset;
        Dirty(companionUid, companionComponent);

        CopySides(owner.Owner, companionUid);
        UpdateFollowTarget((companionUid, companionComponent));
        _dissolveSystem.Materialise(companionUid, entry.MaterialiseDuration);

        if (returned)
        {
            _popupSystem.PopupEntity(
                KsPopupMessage.Create(entry.ReturnedPopup, ("companion", new KsPopupPrototypeName(entry.Prototype))),
                owner.Owner,
                owner.Owner);
        }
    }

    /// <summary>
    ///     The companion takes the owner's side: its NPC factions, so enemies fight it and friends' shots pass
    ///         through it, and shots of its own that pass through friends.
    /// </summary>
    /// <remarks>
    ///     Never a scenario faction: that is what scenario rules count as a side's people (Field Commander's
    ///         defenders, its wave budget and caps, a side wiped out at round end), and a companion isn't one.
    /// </remarks>
    private void CopySides(EntityUid ownerUid, EntityUid companionUid)
    {
        if (_factionMemberQuery.TryComp(ownerUid, out var ownerFactionComponent))
        {
            var companionFactionComponent = EnsureComp<NpcFactionMemberComponent>(companionUid);
            _npcFactionSystem.ClearFactions((companionUid, companionFactionComponent), dirty: false);
            _npcFactionSystem.AddFactions((companionUid, companionFactionComponent),
                new HashSet<ProtoId<NpcFactionPrototype>>(ownerFactionComponent.Factions));
        }

        // One its prototype came with would count it all the same.
        if (_scenarioMemberQuery.HasComp(companionUid))
            RemComp<ScenarioFactionMemberComponent>(companionUid);

        EnsureComp<KsFactionCollisionShooterComponent>(companionUid);
    }

    /// <summary>
    ///     Takes a companion away on purpose: it dissolves, and its going isn't counted as being destroyed.
    /// </summary>
    private void Release(Entity<KsCompanionsComponent> owner, int index)
    {
        if (index >= owner.Comp.Spawned.Count || owner.Comp.Spawned[index] is not { } companionUid)
            return;

        owner.Comp.Spawned[index] = null;
        Dissolve(companionUid, owner.Comp.Companions[index].DissolveDuration);
    }

    private void Dissolve(EntityUid companionUid, TimeSpan? duration)
    {
        if (_companionQuery.TryComp(companionUid, out var companionComponent))
            companionComponent.Released = true;

        // A hovering one is no longer steered, and nothing slows a weightless body down: it stops where it is.
        if (companionComponent?.Mode == KsCompanionMode.Hover)
            _physicsSystem.SetLinearVelocity(companionUid, Vector2.Zero);

        // Nothing more from it while it fades.
        if (_htnQuery.TryComp(companionUid, out var htnComponent))
            _npcSystem.SleepNPC(companionUid, htnComponent);

        _dissolveSystem.Dissolve(companionUid, duration);
    }

    #endregion

    #region Companion

    // A by-value event, so no Entity<T> handler (RA0054).
    [SubscribeLocalEvent]
    private void OnCompanionMobStateChanged(EntityUid uid, KsCompanionComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || component.Released)
            return;

        // Destroyed but left whole: its wreck dissolves.
        Destroyed((uid, component));
        // Without its owner's entry, at the dissolve's own default length.
        if (component.OwnerUid is { } ownerUid && _companionsQuery.TryComp(ownerUid, out var companionsComponent) &&
            component.Index < companionsComponent.Companions.Count)
            Dissolve(uid, companionsComponent.Companions[component.Index].DissolveDuration);
        else
            Dissolve(uid, duration: null);
    }

    /// <summary>
    ///     A companion let go (dissolving after its owner's death, or its wreck) and then picked up or put in something
    ///         has its dissolve called off by <see cref="KsDissolveSystem"/>, which would keep it forever: it is deleted
    ///         instead.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnCompanionInserted(Entity<KsCompanionComponent> companion, ref EntGotInsertedIntoContainerMessage args)
    {
        if (companion.Comp.Released)
            QueueDel(companion.Owner);
    }

    [SubscribeLocalEvent]
    private void OnCompanionShutdown(Entity<KsCompanionComponent> companion, ref ComponentShutdown args)
    {
        // Gone without being let go: destroyed outright.
        if (!companion.Comp.Released)
            Destroyed(companion);
    }

    /// <summary>
    ///     The companion was destroyed: the owner hears of it, and it comes back after its delay.
    /// </summary>
    private void Destroyed(Entity<KsCompanionComponent> companion)
    {
        companion.Comp.Released = true;

        if (companion.Comp.OwnerUid is not { } ownerUid ||
            !_companionsQuery.TryComp(ownerUid, out var companionsComponent) ||
            companionsComponent.LifeStage > ComponentLifeStage.Running ||
            companion.Comp.Index >= companionsComponent.Spawned.Count ||
            companionsComponent.Spawned[companion.Comp.Index] != companion.Owner)
            return;

        var index = companion.Comp.Index;
        var entry = companionsComponent.Companions[index];
        companionsComponent.Spawned[index] = null;
        companionsComponent.RespawnTimes[index] = _gameTiming.CurTime + entry.RespawnDelay;

        if (TerminatingOrDeleted(ownerUid))
            return;

        _popupSystem.PopupEntity(
            KsPopupMessage.Create(entry.DestroyedPopup,
                ("companion", new KsPopupPrototypeName(entry.Prototype)),
                ("seconds", (int)Math.Ceiling(entry.RespawnDelay.TotalSeconds))),
            ownerUid,
            ownerUid,
            PopupType.MediumCaution);
    }

    /// <summary>
    ///     Steers every hovering companion after its hover point: the owner's spot on its grid or map (followed into a
    ///         locker or pod) plus the entry's offset, in that grid's frame, so the owner turning doesn't swing it round.
    ///         It moves at <see cref="KsCompanionEntry.HoverStiffness"/> times the distance left, capped at
    ///         <see cref="KsCompanionEntry.HoverMaxSpeed"/>, so it trails and settles; past
    ///         <see cref="KsCompanionEntry.HoverSnapDistance"/>, or on another map, it is put there at once.
    /// </summary>
    /// <remarks>
    ///     Its velocity is relative to its parent, so on the owner's grid that grid's own motion carries it for free.
    ///         Over another parent (the owner just stepped onto a shuttle) the difference between the two frames'
    ///         velocities is added, so a moving grid doesn't leave it behind.
    /// </remarks>
    private void UpdateHovering(float frameTime)
    {
        var query = EntityQueryEnumerator<KsCompanionComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var companionComponent, out var physicsComponent, out var transformComponent))
        {
            if (companionComponent.Mode != KsCompanionMode.Hover ||
                companionComponent.Released ||
                companionComponent.OwnerUid is not { } ownerUid ||
                !_companionsQuery.TryComp(ownerUid, out var companionsComponent) ||
                companionComponent.Index >= companionsComponent.Companions.Count ||
                TerminatingOrDeleted(ownerUid))
                continue;

            Hover((uid, physicsComponent, transformComponent), ownerUid, companionsComponent.Companions[companionComponent.Index], frameTime);
        }
    }

    private void Hover(Entity<PhysicsComponent, TransformComponent> companion, EntityUid ownerUid, KsCompanionEntry entry, float frameTime)
    {
        // Put in something (picked up, boxed): it stays there, rather than being pulled back out.
        if (_containerSystem.IsEntityInContainer(companion.Owner))
            return;

        var hoverCoordinates = _transformSystem.GetMoverCoordinates(ownerUid).Offset(entry.Offset);
        var hoverMapCoordinates = _transformSystem.ToMapCoordinates(hoverCoordinates);
        if (hoverMapCoordinates.MapId == MapId.Nullspace)
            return;

        var companionMapCoordinates = _transformSystem.GetMapCoordinates(companion.Owner, companion.Comp2);
        var toHover = hoverMapCoordinates.Position - companionMapCoordinates.Position;
        var distance = toHover.Length();

        if (companionMapCoordinates.MapId != hoverMapCoordinates.MapId || distance > entry.HoverSnapDistance)
        {
            _transformSystem.SetCoordinates(companion.Owner, hoverCoordinates);
            _physicsSystem.SetLinearVelocity(companion.Owner, Vector2.Zero, body: companion.Comp1);
            return;
        }

        var velocity = Vector2.Zero;
        if (distance > HoverSettleDistance)
        {
            // Never so fast that it passes the point in one tick, whatever the stiffness.
            var speed = MathF.Min(distance * entry.HoverStiffness, MathF.Min(entry.HoverMaxSpeed, distance / frameTime));
            velocity = toHover * (speed / distance);
        }

        if (companion.Comp2.ParentUid != hoverCoordinates.EntityId)
        {
            velocity += _physicsSystem.GetMapLinearVelocity(hoverCoordinates) -
                        _physicsSystem.GetMapLinearVelocity(companion.Comp2.Coordinates);
        }

        _physicsSystem.SetLinearVelocity(companion.Owner, velocity, body: companion.Comp1);
    }

    private void UpdateFollowTarget(Entity<KsCompanionComponent> companion)
    {
        if (companion.Comp.Mode != KsCompanionMode.Follow ||
            companion.Comp.OwnerUid is not { } ownerUid ||
            !_htnQuery.TryComp(companion.Owner, out var htnComponent))
            return;

        var targetUid = companion.Comp.FollowOverride is { } overrideUid && !TerminatingOrDeleted(overrideUid)
            ? overrideUid
            : ownerUid;

        _npcSystem.SetBlackboard(companion.Owner, NPCBlackboard.FollowTarget, new EntityCoordinates(targetUid, Vector2.Zero), htnComponent);
    }

    #endregion
}
