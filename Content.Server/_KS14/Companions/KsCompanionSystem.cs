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
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Companions;

/// <summary>
///     Spawns and keeps an owner's companions (<see cref="KsCompanionsComponent"/>): each materialises beside the
///         owner (or on it, <see cref="KsCompanionMode.Attached"/>), takes the owner's NPC and scenario factions, and
///         its shots pass through the owner's side. One destroyed comes back after its entry's delay, with a popup to
///         the owner; all of them dissolve when the owner dies and come back when it is revived.
/// </summary>
public sealed partial class KsCompanionSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private KsDissolveSystem _dissolveSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private NPCSystem _npcSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<KsCompanionComponent> _companionQuery = default!;
    [Dependency] private EntityQuery<KsCompanionsComponent> _companionsQuery = default!;
    [Dependency] private EntityQuery<HTNComponent> _htnQuery = default!;
    [Dependency] private EntityQuery<NpcFactionMemberComponent> _factionMemberQuery = default!;
    [Dependency] private EntityQuery<ScenarioFactionMemberComponent> _scenarioMemberQuery = default!;

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
    ///         with null. Does nothing to an attached one.
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

        if (args.OldMobState == MobState.Dead)
        {
            component.RespawnTimes.Clear();
            SpawnMissing(owner, returned: false);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

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
        var coordinates = entry.Mode == KsCompanionMode.Attached
            ? new EntityCoordinates(owner.Owner, entry.Offset)
            : _transformSystem.GetMoverCoordinates(owner.Owner);

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
    ///     The companion takes the owner's side: its NPC factions, its scenario faction, and shots that pass through
    ///         its own.
    /// </summary>
    private void CopySides(EntityUid ownerUid, EntityUid companionUid)
    {
        if (_factionMemberQuery.TryComp(ownerUid, out var ownerFactionComponent))
        {
            var companionFactionComponent = EnsureComp<NpcFactionMemberComponent>(companionUid);
            _npcFactionSystem.ClearFactions((companionUid, companionFactionComponent), dirty: false);
            _npcFactionSystem.AddFactions((companionUid, companionFactionComponent),
                new HashSet<ProtoId<NpcFactionPrototype>>(ownerFactionComponent.Factions));
        }

        if (_scenarioMemberQuery.TryComp(ownerUid, out var ownerScenarioComponent))
        {
            if (_scenarioMemberQuery.TryComp(companionUid, out var companionScenarioComponent))
            {
                companionScenarioComponent.Id = ownerScenarioComponent.Id;
                Dirty(companionUid, companionScenarioComponent);
            }
            else
            {
                AddComp(companionUid, new ScenarioFactionMemberComponent { Id = ownerScenarioComponent.Id });
            }
        }

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

    private void Dissolve(EntityUid companionUid, TimeSpan duration)
    {
        if (_companionQuery.TryComp(companionUid, out var companionComponent))
            companionComponent.Released = true;

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
        if (component.OwnerUid is { } ownerUid && _companionsQuery.TryComp(ownerUid, out var companionsComponent) &&
            component.Index < companionsComponent.Companions.Count)
            Dissolve(uid, companionsComponent.Companions[component.Index].DissolveDuration);
        else
            Dissolve(uid, TimeSpan.FromSeconds(1.5));
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
    ///     An attached companion knocked off its spot is put straight back.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnCompanionMove(Entity<KsCompanionComponent> companion, ref MoveEvent args)
    {
        if (companion.Comp.Mode != KsCompanionMode.Attached ||
            companion.Comp.Released ||
            companion.Comp.OwnerUid is not { } ownerUid ||
            TerminatingOrDeleted(ownerUid) ||
            TerminatingOrDeleted(companion.Owner))
            return;

        if (args.NewPosition.EntityId == ownerUid && args.NewPosition.Position == companion.Comp.Offset)
            return;

        _transformSystem.SetCoordinates(companion.Owner, new EntityCoordinates(ownerUid, companion.Comp.Offset));
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
