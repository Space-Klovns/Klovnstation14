using Content.Server._KS14.NPC.Squad.Tactics;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Disposal.Components;
using Content.Shared.Disposal.Unit;
using Content.Shared.Interaction;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;

namespace Content.Server._KS14.NPC.HTN.PrimitiveTasks.Operators.Interactions;

/// <summary>
///     Opens the storage at <see cref="TargetKey"/> - a locker, crate, body bag - straight through the storage
///         system: no free hand needed, so an NPC holding a gun can do it, and an open one is left open rather than
///         toggled shut. Succeeds if it is open afterwards; fails if it is out of reach, or welded or locked.
///     A disposal unit, which has no door to open, is emptied instead, and the squad told it has been searched
///         (<see cref="NpcSquadTacticsSystem.NoteEmptied"/>).
/// </summary>
/// <remarks>
///     Not <c>InteractWithOperator</c>: with something in the active hand that interacts using the held item, which
///         does not open a locker, and with an empty hand it toggles - shutting one that is already open.
/// </remarks>
public sealed partial class OpenEntityStorageOperator : HTNOperator
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private NpcSquadTacticsSystem _npcSquadTacticsSystem = default!;
    [Dependency] private SharedDisposalUnitSystem _disposalUnitSystem = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorageSystem = default!;
    [Dependency] private SharedInteractionSystem _interactionSystem = default!;

    [DataField(required: true)]
    public string TargetKey = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (blackboard.TryGetEntity(TargetKey, _entityManager, out var disposalUid) &&
            _entityManager.TryGetComponent<DisposalUnitComponent>(disposalUid, out var disposalComponent))
            return EmptyDisposal(blackboard.GetOwner(), (disposalUid, disposalComponent));

        if (!blackboard.TryGetEntity(TargetKey, _entityManager, out var storageUid) ||
            !_entityManager.TryGetComponent<EntityStorageComponent>(storageUid, out var storageComponent))
            return HTNOperatorStatus.Failed;

        if (storageComponent.Open)
            return HTNOperatorStatus.Finished;

        var ownerUid = blackboard.GetOwner();

        if (!_interactionSystem.InRangeUnobstructed(ownerUid, storageUid))
            return HTNOperatorStatus.Failed;

        return _entityStorageSystem.TryOpenStorage(ownerUid, storageUid, silent: true)
            ? HTNOperatorStatus.Finished
            : HTNOperatorStatus.Failed;
    }

    private HTNOperatorStatus EmptyDisposal(EntityUid ownerUid, Entity<DisposalUnitComponent> disposal)
    {
        if (!_interactionSystem.InRangeUnobstructed(ownerUid, disposal.Owner))
            return HTNOperatorStatus.Failed;

        _disposalUnitSystem.TryEjectContents(disposal.Owner, disposal.Comp);
        _npcSquadTacticsSystem.NoteEmptied(ownerUid, disposal.Owner);
        return HTNOperatorStatus.Finished;
    }
}
