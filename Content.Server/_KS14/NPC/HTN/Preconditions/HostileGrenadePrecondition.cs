using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.NPC.Systems;
using Content.Shared.Throwing;
using Content.Shared.Trigger.Components;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met unless the grenade at <see cref="Key"/> was lit by the owner or a friend of it: by whoever set its fuse off, or
///         failing that whoever threw it. One nobody can tell the thrower of counts as a hostile's. For calling out an enemy
///         grenade without calling out the squad's own.
/// </summary>
public sealed partial class HostileGrenadePrecondition : KsInvertiblePrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcFactionSystem _npcFactionSystem = default!;

    [Dependency] private EntityQuery<ThrownItemComponent> _thrownItemQuery = default!;
    [Dependency] private EntityQuery<TimerTriggerComponent> _timerTriggerQuery = default!;

    [DataField(required: true)]
    public string Key = default!;

    protected override bool IsMetRaw(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(Key, out var grenadeUid, _entityManager))
            return false;

        var lighterUid = _timerTriggerQuery.TryComp(grenadeUid, out var timerTriggerComponent) ? timerTriggerComponent.User : null;
        if (lighterUid == null && _thrownItemQuery.TryComp(grenadeUid, out var thrownItemComponent))
            lighterUid = thrownItemComponent.Thrower;

        if (lighterUid is not { } throwerUid || _entityManager.Deleted(throwerUid))
            return true;

        var ownerUid = blackboard.GetOwner();
        return throwerUid != ownerUid && !_npcFactionSystem.IsEntityFriendly(ownerUid, throwerUid);
    }
}
