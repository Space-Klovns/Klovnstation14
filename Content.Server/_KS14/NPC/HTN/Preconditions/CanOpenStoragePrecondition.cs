using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;

namespace Content.Server._KS14.NPC.HTN.Preconditions;

/// <summary>
///     Met when the owner could open the storage at <see cref="TargetKey"/> - a locker, crate, body bag - if it
///         were in reach: it has hands, and the storage is not welded or locked. Also met if it is already open.
/// </summary>
public sealed partial class CanOpenStoragePrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorageSystem = default!;

    [DataField(required: true)]
    public string TargetKey = default!;

    [DataField]
    public bool Invert;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var storageUid, _entityManager) ||
            !_entityManager.HasComponent<EntityStorageComponent>(storageUid))
            return Invert;

        var ownerUid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var canOpen = _entityStorageSystem.IsOpen(storageUid) || _entityStorageSystem.CanOpen(ownerUid, storageUid, silent: true);

        return canOpen != Invert;
    }
}
