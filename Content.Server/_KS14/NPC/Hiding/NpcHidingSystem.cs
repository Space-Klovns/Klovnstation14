using Content.Server._KS14.NPC.Recovery;
using Content.Server._KS14.NPC.Systems;
using Content.Server._KS14.NPC.Utility;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage.Systems;
using Content.Shared.Lock;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tools.Components;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Hiding;

/// <summary>
///     NPCs hiding in closets to heal (<see cref="NpcClosetHealerComponent"/>). Getting in is the HTN's
///         (<c>EnterEntityStorageOperator</c>, then the meds, then <c>HideOperator</c>); this works out when to get out, and
///         lets it out: once its meds have done what they will, a hostile comes near enough to hear, it is hurt in there, or
///         it has been in too long. One that cannot act (stunned, asleep, down) stays in for all but the last.
///     <para>
///         Nothing an NPC does inside a closet gets it out by itself: moving inside one opens it only for a player's keys.
///             So it is let out here, and taking <see cref="NpcHidingComponent"/> off however that happens - a plan cut
///             short included - lets it out too.
///     </para>
/// </summary>
public sealed partial class NpcHidingSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;
    [Dependency] private NpcRecoverySystem _npcRecoverySystem = default!;
    [Dependency] private NpcSensorSystem _npcSensorSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorageSystem = default!;

    [Dependency] private EntityQuery<EntityStorageComponent> _entityStorageQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private EntityQuery<NpcClosetHealerComponent> _closetHealerQuery = default!;
    [Dependency] private EntityQuery<NpcHidingComponent> _hidingQuery = default!;
    [Dependency] private EntityQuery<LockComponent> _lockQuery = default!;
    [Dependency] private EntityQuery<WeldableComponent> _weldableQuery = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.25);

    private readonly HashSet<EntityUid> _hostiles = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NpcHidingComponent, ComponentShutdown>(OnHidingShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _gameTiming.CurTime;
        var enumerator = EntityQueryEnumerator<NpcHidingComponent, NpcClosetHealerComponent>();

        while (enumerator.MoveNext(out var uid, out var hidingComponent, out var closetHealerComponent))
        {
            if (!NpcThrottle.TryConsume(ref hidingComponent.NextCheck, now, CheckInterval))
                continue;

            if (ShouldComeOut((uid, hidingComponent, closetHealerComponent), now))
                StopHiding(uid);
        }
    }

    /// <summary>
    ///     Whether <paramref name="storageUid"/> looks like somewhere to hide that it could get out of again by itself: not
    ///         welded or locked, room left, and nobody in there already. Only reads what is so, for planning: whether it
    ///         really goes in is only known by trying (<see cref="TryHideIn"/>).
    /// </summary>
    public bool IsHidingPlace(EntityUid storageUid)
    {
        if (!_entityStorageQuery.TryComp(storageUid, out var storageComponent) ||
            _containerSystem.IsEntityOrParentInContainer(storageUid) ||
            _weldableQuery.TryComp(storageUid, out var weldableComponent) && weldableComponent.IsWelded ||
            _lockQuery.TryComp(storageUid, out var lockComponent) && lockComponent.Locked ||
            storageComponent.Contents.ContainedEntities.Count >= storageComponent.Capacity)
            return false;

        foreach (var containedUid in storageComponent.Contents.ContainedEntities)
        {
            if (_mobStateQuery.HasComp(containedUid))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Gets <paramref name="uid"/> into <paramref name="storageUid"/>, shutting it behind it, and has it stay there.
    /// </summary>
    /// <returns>Whether it is in.</returns>
    public bool TryHideIn(EntityUid uid, EntityUid storageUid)
    {
        // It has to be able to get out again by itself.
        if (!IsHidingPlace(storageUid) || !_entityStorageSystem.CanOpen(uid, storageUid, silent: true))
            return false;

        // Stood in it with it open, shutting it takes it in, as it does anyone.
        if (_entityStorageSystem.IsOpen(storageUid) && !_entityStorageSystem.TryCloseStorage(storageUid, uid))
            return false;

        // Shut, so what fits is known now. Not in by shutting it, it is put in: only ever from where it stands on it
        //      (EnterEntityStorageOperator), or it is seen to jump in.
        if (!IsIn(uid, storageUid) &&
            (!_entityStorageSystem.CanInsert(uid, storageUid) || !_entityStorageSystem.Insert(uid, storageUid)))
            return false;

        var hidingComponent = EnsureComp<NpcHidingComponent>(uid);
        hidingComponent.StorageUid = storageUid;
        hidingComponent.EnteredAt = _gameTiming.CurTime;
        hidingComponent.Damage = _damageableSystem.GetTotalDamage(uid);
        hidingComponent.WasRecovering = false;
        hidingComponent.NextCheck = _gameTiming.CurTime + CheckInterval;
        return true;
    }

    /// <summary>
    ///     Whether a hostile is near enough <paramref name="uid"/> that, hidden, it would come straight out again: within its
    ///         <see cref="NpcClosetHealerComponent.ExitRange"/>, where one in a closet hears footsteps outside. Hiding then
    ///         was a hop in and out again for nothing.
    /// </summary>
    public bool IsHostileInEarshot(EntityUid uid)
    {
        if (!_closetHealerQuery.TryComp(uid, out var closetHealerComponent))
            return false;

        _hostiles.Clear();
        _npcFactionSystem.GetNearbyHostiles(uid, closetHealerComponent.ExitRange, _hostiles);
        return _hostiles.Count > 0;
    }

    public bool IsHiding(EntityUid uid)
    {
        return _hidingQuery.HasComp(uid);
    }

    /// <summary>
    ///     Gets <paramref name="uid"/> out, if it is hiding. Does nothing for one that is not.
    /// </summary>
    public void StopHiding(EntityUid uid)
    {
        RemCompDeferred<NpcHidingComponent>(uid);
    }

    private bool IsIn(EntityUid uid, EntityUid storageUid)
    {
        return _containerSystem.TryGetContainingContainer(uid, out var container) && container.Owner == storageUid;
    }

    private bool ShouldComeOut(Entity<NpcHidingComponent, NpcClosetHealerComponent> entity, TimeSpan now)
    {
        var (uid, hidingComponent, closetHealerComponent) = entity;

        if (TerminatingOrDeleted(hidingComponent.StorageUid) ||
            !_containerSystem.TryGetContainingContainer(uid, out var container) ||
            container.Owner != hidingComponent.StorageUid ||
            now - hidingComponent.EnteredAt >= closetHealerComponent.MaxTime)
            return true;

        // Stunned, asleep or down, it does not open a door and step out, whatever is outside: only its time being up, above,
        //      has it let out all the same. What it could not answer is still so when it can act again.
        if (!_actionBlockerSystem.CanInteract(uid, null))
            return false;

        // Done once the meds have done what they will. They are used once it is in, so give them a moment to start.
        if (_npcRecoverySystem.IsRecovering(uid))
            hidingComponent.WasRecovering = true;
        else if (hidingComponent.WasRecovering || now - hidingComponent.EnteredAt >= closetHealerComponent.MedsGrace)
            return true;

        if (_damageableSystem.GetTotalDamage(uid) > hidingComponent.Damage)
            return true;

        // Heard, not seen: footsteps right outside, which anyone in a closet would.
        _hostiles.Clear();
        _npcFactionSystem.GetNearbyHostiles(uid, closetHealerComponent.ExitRange, _hostiles);
        return _hostiles.Count > 0;
    }

    private void OnHidingShutdown(Entity<NpcHidingComponent> entity, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(entity.Owner))
            return;

        var storageUid = entity.Comp.StorageUid;

        if (!TerminatingOrDeleted(storageUid) &&
            _containerSystem.TryGetContainingContainer(entity.Owner, out var container) &&
            container.Owner == storageUid)
            _entityStorageSystem.TryOpenStorage(entity.Owner, storageUid, silent: true);

        _npcSensorSystem.RequestReplan(entity.Owner);
    }
}
