using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._KS14.NPC.Pushing;

/// <summary>
///     NPCs pushing loose things - closets, crates - out of their way, for NPCs whose paths may (the <c>NavPush</c>
///         blackboard key).
///     <list type="bullet">
///         <item>The navmesh marks a tile blocked only by things anyone could push (<see cref="IsPushable"/> with no
///             pusher) as <see cref="Shared.NPC.PathfindingBreadcrumbFlag.Pushable"/>. Paths cross it at a cost, rather
///             than not at all.</item>
///         <item>Steering, on reaching such a tile, asks whether this NPC can push what is there
///             (<see cref="IsPushable"/> with the NPC). If so, it pushes (<see cref="TryPush"/>) until the tile is
///             clear. If not, it remembers it (<see cref="ReportUnpushable"/>) and its next path goes round.</item>
///     </list>
///     What can be pushed, and how, is open to other systems through <see cref="NpcPushableAttemptEvent"/> and
///         <see cref="NpcPushObstacleEvent"/>.
/// </summary>
public sealed partial class NpcPushSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedCombatModeSystem _combatModeSystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private SharedMeleeWeaponSystem _meleeWeaponSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;

    [Dependency] private EntityQuery<CombatModeComponent> _combatModeQuery = default!;
    [Dependency] private EntityQuery<NpcPusherComponent> _pusherQuery = default!;
    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    /// <summary>
    ///     Whether <paramref name="obstacleUid"/> can be pushed out of the way: by anyone, with no
    ///         <paramref name="pusherUid"/>, or by that NPC. Loose bodies that physics moves can be, unless something
    ///         says otherwise (<see cref="NpcPushableAttemptEvent"/>). Anchored and static ones, and mobs, cannot. Pure:
    ///         safe from the navmesh build and from planning.
    /// </summary>
    public bool IsPushable(EntityUid obstacleUid, EntityUid? pusherUid)
    {
        if (!_physicsQuery.TryComp(obstacleUid, out var physicsComponent) ||
            physicsComponent.BodyType != BodyType.Dynamic ||
            Transform(obstacleUid).Anchored)
            return false;

        if (pusherUid is { } npcUid && IsRemembered(npcUid, obstacleUid))
            return false;

        var ev = new NpcPushableAttemptEvent(pusherUid);
        RaiseLocalEvent(obstacleUid, ref ev);
        return !ev.Cancelled;
    }

    /// <summary>
    ///     How long a push may lapse and still be the same push. See <see cref="NpcPusherComponent.PushingUid"/>.
    /// </summary>
    private static readonly TimeSpan PushLapse = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How far, in tiles, what it pushes must have moved for the push to count as getting somewhere. See
    ///         <see cref="NpcPusherComponent.GiveUpAfter"/>.
    /// </summary>
    private const float PushProgress = 0.5f;

    /// <summary>
    ///     <paramref name="npcUid"/> pushes <paramref name="obstacleUid"/> away from itself, or keeps trying: something
    ///         else may move it instead (<see cref="NpcPushObstacleEvent"/>), and a shove waits out its cooldown, may
    ///         fail, and needs the NPC within reach. Returns false if it cannot shove at all - no combat mode that
    ///         disarms, or nothing to shove with - or has pushed it for <see cref="NpcPusherComponent.GiveUpAfter"/>
    ///         without it budging, so steering can give up on it.
    /// </summary>
    public bool TryPush(EntityUid npcUid, EntityUid obstacleUid)
    {
        var pusherComponent = EnsureComp<NpcPusherComponent>(npcUid);
        var now = _gameTiming.CurTime;

        var obstaclePosition = _transformSystem.GetWorldPosition(obstacleUid);
        if (pusherComponent.PushingUid != obstacleUid ||
            now - pusherComponent.LastPushedAt > PushLapse ||
            (obstaclePosition - pusherComponent.PushingFrom).LengthSquared() > PushProgress * PushProgress)
        {
            pusherComponent.PushingUid = obstacleUid;
            pusherComponent.PushingFrom = obstaclePosition;
            pusherComponent.PushingSince = now;
        }

        pusherComponent.LastPushedAt = now;
        if (now - pusherComponent.PushingSince > pusherComponent.GiveUpAfter)
            return false;

        var ev = new NpcPushObstacleEvent(npcUid);
        RaiseLocalEvent(obstacleUid, ref ev);

        if (ev.Handled)
            return true;

        if (!_combatModeQuery.TryComp(npcUid, out var combatModeComponent) ||
            combatModeComponent.CanDisarm != true ||
            !_meleeWeaponSystem.TryGetWeapon(npcUid, out var weaponUid, out var meleeWeaponComponent))
            return false;

        // Swung too soon, or out of reach, a shove still spends its cooldown: hold it until it can land.
        if (meleeWeaponComponent.NextAttack > now ||
            !_interactionSystem.InRangeUnobstructed(npcUid, obstacleUid, meleeWeaponComponent.Range))
            return true;

        var wasInCombatMode = combatModeComponent.IsInCombatMode;
        _combatModeSystem.SetInCombatMode(npcUid, true, combatModeComponent);
        _meleeWeaponSystem.AttemptDisarmAttack(npcUid, weaponUid, meleeWeaponComponent, obstacleUid);
        _combatModeSystem.SetInCombatMode(npcUid, wasInCombatMode, combatModeComponent);
        return true;
    }

    /// <summary>
    ///     <paramref name="npcUid"/> walked up to <paramref name="obstacleUid"/> and could not push it. Its paths go
    ///         round it for a while (see <see cref="GetUnpushable"/>).
    /// </summary>
    public void ReportUnpushable(EntityUid npcUid, EntityUid obstacleUid)
    {
        var pusherComponent = EnsureComp<NpcPusherComponent>(npcUid);
        var now = _gameTiming.CurTime;

        foreach (var (rememberedUid, expiresAt) in pusherComponent.Unpushable)
        {
            if (expiresAt <= now)
                pusherComponent.Unpushable.Remove(rememberedUid);
        }

        pusherComponent.Unpushable[obstacleUid] = now + pusherComponent.ForgetAfter;
        pusherComponent.PushingUid = null;
    }

    /// <summary>
    ///     Adds what <paramref name="npcUid"/>'s paths go round for now to <paramref name="obstacleUids"/>. See
    ///         <see cref="ReportUnpushable"/>.
    /// </summary>
    public void GetUnpushable(EntityUid npcUid, List<EntityUid> obstacleUids)
    {
        if (!_pusherQuery.TryComp(npcUid, out var pusherComponent))
            return;

        var now = _gameTiming.CurTime;
        foreach (var (obstacleUid, expiresAt) in pusherComponent.Unpushable)
        {
            if (expiresAt > now && !TerminatingOrDeleted(obstacleUid))
                obstacleUids.Add(obstacleUid);
        }
    }

    private bool IsRemembered(EntityUid npcUid, EntityUid obstacleUid)
    {
        return _pusherQuery.TryComp(npcUid, out var pusherComponent) &&
            pusherComponent.Unpushable.TryGetValue(obstacleUid, out var expiresAt) &&
            expiresAt > _gameTiming.CurTime;
    }
}
